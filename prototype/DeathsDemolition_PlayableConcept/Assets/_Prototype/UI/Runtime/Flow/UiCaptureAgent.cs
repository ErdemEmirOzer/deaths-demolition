using System;
using System.Collections;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Player-side screen captures for the UI quality gate (WP-022C, directive §14).
    ///
    ///   DeathsDemolition.app/Contents/MacOS/... -ddUiCapture selection -ddUiCaptureOut /tmp/sel.png -ddSize 1920x1080
    ///
    /// Screens: <c>mainmenu</c>, <c>settings</c> / <c>profile</c> / <c>achievements</c> / <c>statistics</c> (main menu with that pane),
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

        /// <summary>WP-022E: <c>-ddUiPickTitle &lt;id&gt;</c> selects an already-unlocked title before the capture (name-plate evidence; the profile is restored after every capture run, as after every smoke).</summary>
        private const string PickTitleFlag = "-ddUiPickTitle";

        /// <summary>
        /// WP-022F: <c>-ddUiCapture hud</c> is an OBSERVER - it drives nothing and never quits.
        /// Pair it with the WP-026 bot (<c>-ddQaBot</c>): frames are written into the folder
        /// given by <c>-ddUiCaptureOut</c> as the run reaches <c>-ddUiCaptureRounds 1,4,8,12</c>
        /// (a few seconds into each round: <c>hud-r4.png</c>), the first upgrade screen
        /// (<c>upgrade.png</c>), the first Special on the ground / in hand
        /// (<c>special-ground.png</c>, <c>special-active.png</c>), the first threat chip
        /// (<c>threat.png</c>) and the first low-health state (<c>low-health.png</c>).
        /// </summary>
        private const string RoundsFlag = "-ddUiCaptureRounds";
        private const string BotFlag = "-ddQaBot";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string screen = Arg(ScreenFlag);
            if (string.IsNullOrEmpty(screen))
            {
                return;
            }

            // A capture changes settings and records loads for its frames: all of it goes to a
            // process-local folder, never the player's ui-settings.json (user-data rule).
            UiDataPaths.UseProcessLocalRoot();
            ApplyCaptureSettings();
            var carrier = new GameObject("UiCaptureAgent");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            if (screen.ToLowerInvariant() == "hud")
            {
                carrier.AddComponent<HudObserver>().Configure(Arg(OutFlag), Arg(RoundsFlag), Arg(DelayFlag), Arg(BotFlag) != null);
                return;
            }

            carrier.AddComponent<Driver>().Configure(screen.ToLowerInvariant(), Arg(OutFlag), Arg(SizeFlag), Arg(DelayFlag), Arg(PickTitleFlag));
        }

        /// <summary>
        /// WP-068 slice 6 · the capture matrix (§12): <c>-ddUiTextScale 1.25</c>,
        /// <c>-ddUiScale 1.2</c>, <c>-ddUiPalette cb</c>, <c>-ddUiReduceFlashes 1</c>,
        /// <c>-ddUiReducedMotion 1</c>, set through <see cref="UiSettings"/>
        /// like a player would, into this process's own folder, before the first screen builds.
        /// </summary>
        private static void ApplyCaptureSettings()
        {
            float value;
            string text = Arg("-ddUiTextScale");
            if (!string.IsNullOrEmpty(text) && float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                UiSettings.TextScale = value;
            }

            string scale = Arg("-ddUiScale");
            if (!string.IsNullOrEmpty(scale) && float.TryParse(scale, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                UiSettings.UiScale = value;
            }

            if (Arg("-ddUiPalette") == "cb")
            {
                UiSettings.ColorPalette = 1;
            }

            if (Arg("-ddUiReduceFlashes") == "1")
            {
                UiSettings.ReduceFlashes = true;
            }

            if (Arg("-ddUiReducedMotion") == "1")
            {
                UiSettings.ReducedMotion = true;
            }

            // -ddUiScheme pad: the prompts drawn for a gamepad (WP-068 V1 shapes) without a pad attached.
            if (Arg("-ddUiScheme") == "pad")
            {
                UiInputScheme.Force(DD.Prototype.Core.InputAbstraction.InputSchemeKind.Gamepad);
            }

            // -ddUiLook x,y: hold the menu backdrop's parallax at a corner (−1..1), AD 6ab frames.
            string look = Arg("-ddUiLook");
            if (!string.IsNullOrEmpty(look))
            {
                string[] parts = look.Split(',');
                float x, y;
                if (parts.Length == 2
                    && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x)
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y))
                {
                    UiBackdrop.LookOverride = new Vector2(x, y);
                }
            }
        }

        private static void WritePng(string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(path, frame.EncodeToPNG());
            Debug.Log(string.Format("[UiCapture] frame {0} · {1}x{2} · scene {3}", path, frame.width, frame.height, SceneManager.GetActiveScene().name));
            UnityEngine.Object.Destroy(frame);
        }

        /// <summary>
        /// WP-068 8-R · the HUD's screen coverage. The next two frames draw the HUD over a black and then a white backdrop,
        /// on a canvas just under the HUD, so the world is out of the count. A pixel is covered when either frame differs
        /// from its backdrop by more than 3 % in any channel. That counts translucent ink over white and pale strokes over
        /// black alike. Unity's own "Development Build" watermark counts too (≈ 0.05 %).
        /// Two numbers per frame: as seen, and persistent (the transient alarms hidden). Writes the persistent over-black frame
        /// (<c>name-footprint.png</c>) and one JSON line to <c>coverage.jsonl</c>.
        /// </summary>
        internal static IEnumerator Coverage(string folder, string name)
        {
            Canvas canvas = UiFactory.CreateCanvas("[HudCoverageBack]", UiTokens.SortingHud - 1);
            UnityEngine.UI.Image back = UiFactory.NewImage("Back", canvas.transform, UiColor.SurfaceInset);
            UiFactory.Fill(back.rectTransform);
            back.raycastTarget = false;

            // As seen, then persistent: the transient alarms (the Special's final-seconds veil and chip, the damage edges,
            // the banner) hidden for two frames, since the cap is about what stays on screen in combat.
            float[] result = new float[2];
            Texture2D dark = null;
            for (int pass = 0; pass < 2; pass++)
            {
                // Alpha, not SetActive: the HUD re-activates its warning layers every frame.
                var hidden = new System.Collections.Generic.List<CanvasGroup>();
                var alphas = new System.Collections.Generic.List<float>();
                var reactivate = new System.Collections.Generic.List<GameObject>();
                if (pass == 1)
                {
                    RunHudScreen hud = UnityEngine.Object.FindObjectOfType<RunHudScreen>();
                    Transform root = hud != null ? hud.transform.Find(hud.GetType().Name + ".Canvas") : null;
                    string[] transient = { "Warning", "FinalSeconds", "DamageEdges", "Banner", "BannerRoot" };
                    for (int i = 0; root != null && i < transient.Length; i++)
                    {
                        Transform t = root.Find(transient[i]);
                        if (t != null)
                        {
                            // Both: the HUD rewrites the banner's alpha and the warning's active state every frame.
                            CanvasGroup group = UiFactory.Group(t.gameObject);
                            alphas.Add(group.alpha);
                            group.alpha = 0f;
                            hidden.Add(group);
                            if (t.gameObject.activeSelf)
                            {
                                t.gameObject.SetActive(false);
                                reactivate.Add(t.gameObject);
                            }
                        }
                    }
                }

                back.color = Color.black;
                yield return null;
                yield return new WaitForEndOfFrame();
                Texture2D a = ScreenCapture.CaptureScreenshotAsTexture();
                back.color = Color.white;
                yield return null;
                yield return new WaitForEndOfFrame();
                Texture2D b = ScreenCapture.CaptureScreenshotAsTexture();
                for (int i = 0; i < hidden.Count; i++)
                {
                    hidden[i].alpha = alphas[i];
                }

                for (int i = 0; i < reactivate.Count; i++)
                {
                    reactivate[i].SetActive(true);
                }

                result[pass] = CoveredPercent(a, b);
                if (pass == 1)
                {
                    dark = a;
                }
                else
                {
                    UnityEngine.Object.Destroy(a);
                }

                UnityEngine.Object.Destroy(b);
            }

            UnityEngine.Object.Destroy(canvas.gameObject);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name + "-footprint.png"), dark.EncodeToPNG());
            System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;
            File.AppendAllText(Path.Combine(folder, "coverage.jsonl"),
                "{\"frame\": \"" + name + "\", \"coverage\": " + result[1].ToString("0.00", inv) + ", \"coverageAsSeen\": " + result[0].ToString("0.00", inv) +
                ", \"screen\": \"" + dark.width + "x" + dark.height + "\"}\n");
            Debug.Log("[UiCapture] coverage " + name + " " + result[1].ToString("0.00", inv) + " % (as seen " + result[0].ToString("0.00", inv) + " %)");
            UnityEngine.Object.Destroy(dark);
        }

        /// <summary>The share of pixels where the HUD over black or over white differs from its backdrop by more than 3 %.</summary>
        private static float CoveredPercent(Texture2D overBlack, Texture2D overWhite)
        {
            Color32[] a = overBlack.GetPixels32();
            Color32[] b = overWhite.GetPixels32();
            const int tolerance = 8;   // 3 % of 255
            int covered = 0;
            for (int i = 0; i < a.Length && i < b.Length; i++)
            {
                if (a[i].r > tolerance || a[i].g > tolerance || a[i].b > tolerance ||
                    b[i].r < 255 - tolerance || b[i].g < 255 - tolerance || b[i].b < 255 - tolerance)
                {
                    covered++;
                }
            }

            return a.Length > 0 ? 100f * covered / a.Length : 0f;
        }

        /// <summary>WP-022F gameplay-frame observer (see <see cref="RoundsFlag"/>).</summary>
        private sealed class HudObserver : MonoBehaviour
        {
            private string _folder;
            private int[] _rounds = new int[0];
            private float _settle = 3f;
            private bool _bot;
            private int _nextRoundIndex;
            private bool _upgradeDone, _groundDone, _activeDone, _threatDone, _lowDone, _statusDone, _multiThreatDone;
            private RunHudScreen _hud;

            internal void Configure(string folder, string rounds, string delay, bool bot)
            {
                _folder = string.IsNullOrEmpty(folder) ? Path.Combine(Application.persistentDataPath, "ui-captures") : folder;
                _bot = bot;
                float d;
                if (!string.IsNullOrEmpty(delay) && float.TryParse(delay, out d))
                {
                    _settle = d;
                }

                if (!string.IsNullOrEmpty(rounds))
                {
                    string[] parts = rounds.Split(',');
                    var list = new System.Collections.Generic.List<int>(parts.Length);
                    for (int i = 0; i < parts.Length; i++)
                    {
                        int r;
                        if (int.TryParse(parts[i].Trim(), out r) && r > 0)
                        {
                            list.Add(r);
                        }
                    }

                    list.Sort();
                    _rounds = list.ToArray();
                }
            }

            private IEnumerator Start()
            {
                if (!_bot)
                {
                    // No bot: reach the run ourselves (the WP-022C path) and observe round 1.
                    yield return UiCaptureAgent.WaitScene(PrototypeScenes.MainMenu, 20f);
                    yield return new WaitForSecondsRealtime(1f);
                    PrototypeUiFlow.GoToSelection();
                    yield return UiCaptureAgent.WaitScene(PrototypeScenes.Selection, 20f);
                    yield return new WaitForSecondsRealtime(0.5f);
                    PrototypeUiFlow.StartRun();
                }

                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Run, 60f);
                yield return UiCaptureAgent.WaitRunLive(30f);

                float roundEntered = -1f;
                int lastRound = -1;
                while (SceneManager.GetActiveScene().name == PrototypeScenes.Run)
                {
                    RunHudState state = RunEvents.Current;

                    if (state.Round != lastRound)
                    {
                        lastRound = state.Round;
                        roundEntered = Time.realtimeSinceStartup;
                    }

                    // Round frames: settle seconds into the listed rounds, while fighting.
                    if (_nextRoundIndex < _rounds.Length && state.Round >= _rounds[_nextRoundIndex]
                        && state.Phase == RunPhase.Fighting && Time.realtimeSinceStartup - roundEntered >= _settle)
                    {
                        yield return Capture("hud-r" + _rounds[_nextRoundIndex] + ".png");
                        yield return UiCaptureAgent.Coverage(_folder, "hud-r" + _rounds[_nextRoundIndex]);   // WP-068 8-R
                        _nextRoundIndex++;
                    }

                    if (!_upgradeDone && state.Phase == RunPhase.Upgrading)
                    {
                        // The bot skips the shop 0.6 s after IT first sees Upgrading; two frames
                        // is enough for the screen to build and the fade to pass its first step.
                        _upgradeDone = true;
                        yield return null;
                        yield return null;
                        yield return Capture("upgrade.png");
                    }

                    SpecialWeaponHudState special = WeaponEvents.Special;
                    if (!_groundDone && special.Phase == SpecialWeaponPhase.OnGround)
                    {
                        _groundDone = true;
                        yield return new WaitForSecondsRealtime(1.5f);
                        yield return Capture("special-ground.png");
                    }

                    if (!_activeDone && special.Phase == SpecialWeaponPhase.Active)
                    {
                        _activeDone = true;
                        yield return new WaitForSecondsRealtime(1f);
                        yield return Capture("special-active.png");
                    }

                    if (!_threatDone && state.Phase == RunPhase.Fighting && (ThreatHudFeed.Encircled || ThreatHudFeed.RearAttack || ThreatHudFeed.StrongVersionPresent))
                    {
                        _threatDone = true;
                        yield return new WaitForSecondsRealtime(0.3f); // WP-068 8b: past the chip's 140 ms entry
                        yield return Capture("threat.png");
                    }

                    // AD 8b SHOULD-4: the first frame with two or more threat chips live (slot rhythm, shared width).
                    if (!_multiThreatDone && state.Phase == RunPhase.Fighting && Time.frameCount % 15 == 0)
                    {
                        if (_hud == null)
                        {
                            _hud = FindObjectOfType<RunHudScreen>();
                        }

                        int chips = 0;
                        for (int i = 0; _hud != null && i < 3; i++)
                        {
                            chips += _hud.ThreatChipVisible(i) ? 1 : 0;
                        }

                        if (chips >= 2)
                        {
                            _multiThreatDone = true;
                            yield return new WaitForSecondsRealtime(0.3f);
                            yield return Capture("threat-multi.png");
                        }
                    }

                    if (!_lowDone && state.Phase == RunPhase.Fighting && ThreatHudFeed.LowHealth)
                    {
                        _lowDone = true;
                        yield return new WaitForSecondsRealtime(0.2f);
                        yield return Capture("low-health.png");
                        yield return UiCaptureAgent.Coverage(_folder, "low-health");
                    }

                    // WP-022L: first frame with a NET / DAZED enemy status chip on screen (WP-055 states).
                    if (!_statusDone && state.Phase == RunPhase.Fighting && AnyEnemyStatusVisible())
                    {
                        _statusDone = true;
                        yield return Capture("enemy-status.png");
                    }

                    yield return null;
                }

                Debug.Log("[UiCapture] hud observer done · rounds " + _nextRoundIndex + "/" + _rounds.Length + " status " + _statusDone
                          + " upgrade " + _upgradeDone + " special " + _groundDone + "/" + _activeDone + " threat " + _threatDone + " lowHealth " + _lowDone);
                if (!_bot)
                {
                    Application.Quit();
                }
            }

            private static bool AnyEnemyStatusVisible()
            {
                System.Collections.Generic.IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; i < live.Count; i++)
                {
                    ZombieEnemy z = live[i];
                    if (z != null && (z.IsElectrocuted || z.Gel != GelState.None))
                    {
                        return true;
                    }
                }

                return false;
            }

            private IEnumerator Capture(string file)
            {
                yield return new WaitForEndOfFrame();
                WritePng(Path.Combine(_folder, file));
            }
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

        private sealed class Driver : MonoBehaviour
        {
            private string _screen;
            private string _out;
            private string _size;
            private float _delay = 2f;
            private string _pickTitle;

            internal void Configure(string screen, string output, string size, string delay, string pickTitle)
            {
                _screen = screen;
                _pickTitle = pickTitle;
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
                if (_screen == "boot")
                {
                    yield return BootFrames();
                    yield break;
                }

                yield return UiCaptureAgent.WaitScene(PrototypeScenes.MainMenu, 20f);
                yield return new WaitForSecondsRealtime(1f);

                if (!string.IsNullOrEmpty(_pickTitle) && DD.Prototype.Profile.ProfileService.CosmeticsSource != null)
                {
                    bool picked = DD.Prototype.Profile.ProfileService.CosmeticsSource.SelectTitle(_pickTitle);
                    Debug.Log("[UiCapture] pick title " + _pickTitle + ": " + (picked ? "selected" : "refused (locked?)"));
                }

                if (_screen == "display")
                {
                    yield return DisplayRoundTrip();
                    yield break;
                }

                if (_screen == "kit")
                {
                    yield return KitSheet();
                    yield break;
                }

                if (_screen == "loading")
                {
                    yield return LoadingFrames();
                    yield break;
                }

                if (_screen == "ink")
                {
                    yield return InkFrames();
                    yield break;
                }

                if (_screen == "hudstates")
                {
                    yield return HudStates();
                    yield break;
                }

                switch (_screen)
                {
                    case "settings":
                    case "profile":
                    case "achievements":
                    case "statistics":
                    case "controls":
                    case "accessibility":
                        {
                            MainMenuScreen menu = FindObjectOfType<MainMenuScreen>();
                            if (menu != null)
                            {
                                menu.ShowPane(_screen == "settings" ? UiContext.Settings
                                    : _screen == "achievements" ? UiContext.Achievements
                                    : _screen == "statistics" ? UiContext.Statistics
                                    : _screen == "controls" ? UiContext.Controls
                                    : _screen == "accessibility" ? UiContext.Accessibility
                                    : UiContext.Profile);
                            }

                            break;
                        }

                    case "selection":
                        PrototypeUiFlow.GoToSelection();
                        yield return UiCaptureAgent.WaitScene(PrototypeScenes.Selection, 20f);
                        break;

                    case "pause":
                    case "results":
                        PrototypeUiFlow.GoToSelection();
                        yield return UiCaptureAgent.WaitScene(PrototypeScenes.Selection, 20f);
                        yield return new WaitForSecondsRealtime(0.5f);
                        PrototypeUiFlow.StartRun();
                        yield return UiCaptureAgent.WaitScene(PrototypeScenes.Run, 30f);
                        yield return UiCaptureAgent.WaitRunLive(20f);
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

                            yield return UiCaptureAgent.WaitScene(PrototypeScenes.Results, 30f);
                        }

                        break;
                }

                yield return new WaitForSecondsRealtime(_delay);

                // WP-068 slice 6: -ddUiFocus <name> focuses a control first (a scrolled pane, a focus state).
                string focusName = Arg("-ddUiFocus");
                if (!string.IsNullOrEmpty(focusName))
                {
                    GameObject target = GameObject.Find(focusName);
                    UnityEngine.UI.Selectable selectable = target != null ? target.GetComponent<UnityEngine.UI.Selectable>() : null;
                    if (selectable != null)
                    {
                        UiInput.Focus(selectable);
                        yield return new WaitForSecondsRealtime(0.5f);
                    }
                    else
                    {
                        Debug.LogWarning("[UiCapture] -ddUiFocus " + focusName + " not found");
                    }
                }

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

            /// <summary>
            /// WP-022M: <c>-ddUiCapture display</c> - Settings DISPLAY row driven through the
            /// real submit path: windowed → fullscreen → windowed, a frame and a readout per
            /// state, <c>display-report.json</c> beside the frames. Exit 0 when both switches
            /// land and the saved file says windowed, 3 otherwise.
            /// </summary>
            private IEnumerator DisplayRoundTrip()
            {
                // -ddUiCaptureOut is a folder here (it need not exist yet); only a file-like path (".png") names its parent.
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                MainMenuScreen menu = FindObjectOfType<MainMenuScreen>();
                if (menu != null)
                {
                    menu.ShowPane(UiContext.Settings);
                }

                yield return new WaitForSecondsRealtime(1f);
                var lines = new System.Collections.Generic.List<string>();
                lines.Add(Readout("start"));
                bool pass = true;
                bool[] targets = { true, false };
                for (int i = 0; i < targets.Length; i++)
                {
                    GameObject row = GameObject.Find("Display");
                    if (row == null)
                    {
                        lines.Add("\"error\": \"Display row not found\"");
                        pass = false;
                        break;
                    }

                    UnityEngine.EventSystems.ExecuteEvents.Execute(row, new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current), UnityEngine.EventSystems.ExecuteEvents.submitHandler);
                    float deadline = Time.realtimeSinceStartup + 20f;
                    while (UiDisplayMode.Pending.HasValue && Time.realtimeSinceStartup < deadline)
                    {
                        yield return null;
                    }

                    yield return new WaitForSecondsRealtime(1.5f);
                    string id = targets[i] ? "fullscreen" : "windowed";
                    bool reached = targets[i] ? Screen.fullScreenMode != FullScreenMode.Windowed : Screen.fullScreenMode == FullScreenMode.Windowed;
                    pass &= reached;
                    yield return new WaitForEndOfFrame();
                    WritePng(Path.Combine(folder, "display-" + id + ".png"));
                    lines.Add(Readout(id) + ", \"reached\": " + (reached ? "true" : "false") + ", \"step\": \"" + UiDisplayMode.LastStep + "\"");
                }

                // Probe (not part of pass): the RC c4 harness path - FullScreenWindow requested
                // with the display size in pixels, then the same windowed request the Settings row makes.
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
                yield return new WaitForSecondsRealtime(2f);
                lines.Add(Readout("probe-fullscreenwindow"));
                UiDisplayMode.Request(false, 1280, 720);
                float probeDeadline = Time.realtimeSinceStartup + 25f;
                while (UiDisplayMode.Pending.HasValue && Time.realtimeSinceStartup < probeDeadline)
                {
                    yield return null;
                }

                yield return new WaitForSecondsRealtime(1f);
                lines.Add(Readout("probe-windowed") + ", \"reached\": " + (Screen.fullScreenMode == FullScreenMode.Windowed ? "true" : "false") + ", \"step\": \"" + UiDisplayMode.LastStep + "\"");
                UiSettings.Fullscreen = false; // the probe bypassed the file; leave it saying windowed

                string saved = File.Exists(UiSettings.FilePath) ? File.ReadAllText(UiSettings.FilePath) : string.Empty;
                bool savedWindowed = saved.Contains("\"DisplayMode\": 0") || saved.Contains("\"DisplayMode\":0");
                pass &= savedWindowed;
                var json = new System.Text.StringBuilder("{\n \"schema\": \"dd-display-roundtrip-v1\",\n \"pass\": " + (pass ? "true" : "false") + ",\n \"savedDisplayModeWindowed\": " + (savedWindowed ? "true" : "false") + ",\n \"states\": [\n");
                for (int i = 0; i < lines.Count; i++)
                {
                    json.Append("  {").Append(lines[i]).Append(i < lines.Count - 1 ? "},\n" : "}\n");
                }

                json.Append(" ]\n}\n");
                File.WriteAllText(Path.Combine(folder, "display-report.json"), json.ToString());
                Debug.Log("[UiCapture] display round-trip " + (pass ? "PASS" : "FAIL"));
                yield return null;
                Application.Quit(pass ? 0 : 3);
            }

            /// <summary>
            /// WP-068 slice 5: <c>-ddUiCapture kit</c> - the component kit sheet (<see cref="UiKitSheet"/>)
            /// at <c>-ddUiKitPalette default|cb</c> and <c>-ddUiKitScale 1.0|1.25</c> (this process
            /// only, nothing saved): the sheet with <c>-ddUiKitFocus primary|switch|stepper</c> focused
            /// (default primary) to <c>-ddUiCaptureOut</c>, then the modal open over it to the same
            /// name with <c>-modal</c>. <c>-ddUiKitRm 1</c> builds it under Reduced Motion.
            /// </summary>
            private IEnumerator KitSheet()
            {
                UiTokens.Palette = Arg("-ddUiKitPalette") == "cb" ? UiPalette.ColorBlindSafe : UiPalette.Default;
                float scale;
                string scaleArg = Arg("-ddUiKitScale");
                if (!string.IsNullOrEmpty(scaleArg) && float.TryParse(scaleArg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale))
                {
                    UiTokens.TextScale = Mathf.Clamp(scale, UiSettings.TextScaleMin, UiSettings.TextScaleMax);
                }

                UiMotion.ReducedMotion = Arg("-ddUiKitRm") == "1";
                int page = Arg("-ddUiKitPage") == "2" ? 2 : 1;
                UiKitSheet.Handles sheet = UiKitSheet.Build(page);
                string focusArg = Arg("-ddUiKitFocus");
                UnityEngine.UI.Selectable focus = focusArg == "switch" ? sheet.Switch
                    : focusArg == "stepper" ? sheet.Stepper
                    : focusArg == "slider" ? (UnityEngine.UI.Selectable)sheet.Slider
                    : focusArg == "card" ? sheet.CardSurface
                    : focusArg == "ghost" ? sheet.Ghost
                    : sheet.Primary;
                UiInput.Focus(focus);
                UiModal modal = sheet.Modal;
                yield return new WaitForSecondsRealtime(_delay);
                if (sheet.Health != null)
                {
                    // Page 2: a hit on the health bar just before the frame, so the ghost is caught in its 350 ms hold.
                    sheet.Health.SetValue(0.55f);
                    yield return new WaitForSecondsRealtime(0.15f);
                }

                yield return new WaitForEndOfFrame();
                WritePng(_out);
                if (modal == null)
                {
                    yield return null;
                    Debug.Log("[UiCapture] kit done · framebuffer " + Screen.width + "x" + Screen.height);
                    Application.Quit();
                    yield break;
                }

                modal.Open();
                yield return new WaitForSecondsRealtime(1f);
                yield return new WaitForEndOfFrame();
                WritePng(Path.Combine(Path.GetDirectoryName(_out) ?? string.Empty, Path.GetFileNameWithoutExtension(_out) + "-modal.png"));
                yield return null;
                Debug.Log("[UiCapture] kit done · framebuffer " + Screen.width + "x" + Screen.height);
                Application.Quit();
            }

            /// <summary>
            /// WP-068 slice 10b: <c>-ddUiCapture loading</c> - the loading screen in the Player
            /// (folder in -ddUiCaptureOut; <c>-ddUiCaptureRm 1</c> = Reduced Motion): MainMenu →
            /// Selection mid-load and at ENTERING, Selection → Run mid-load (mid plate + weapon
            /// sub-line), then on the run the stall notice and the load error modal shown directly
            /// (they need a stuck load to happen for real). The previous loads are recorded as slow so
            /// both transitions take the SCREEN depth.
            /// </summary>
            private IEnumerator LoadingFrames()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                if (Arg("-ddUiCaptureRm") == "1")
                {
                    UiMotion.ReducedMotion = true;
                }

                UiCurtain curtain = UiCurtain.Instance;

                // CURTAIN depth: Selection measured fast → doors only, caught while shut.
                UiLoadTiming.RecordLoad(PrototypeScenes.Selection, 0.2f);
                PrototypeUiFlow.GoToSelection();
                yield return WaitFor(() => UiCurtain.IsCovering && !curtain.GroupVisible, 5f);
                yield return new WaitForSecondsRealtime(0.15f);
                yield return Frame(folder, "loading-curtain");
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Selection, 20f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                yield return new WaitForSecondsRealtime(0.5f);

                // SCREEN depth into the run: mid plate, weapon sub-line, tip; the ensō at 0 / 0.5 /
                // 0.9 (an artificial 3 s load, §15.7) and the seal's stamp at ENTERING (10b-2).
                UiLoadTiming.RecordLoad(PrototypeScenes.Run, 5f);
                PrototypeUiFlow.ArtificialLoadSeconds = 3f;
                PrototypeUiFlow.StartRun();
                yield return WaitFor(() => curtain.GroupVisible && curtain.EyebrowText == "LOADING", 10f);
                yield return new WaitForSecondsRealtime(0.25f);
                yield return Frame(folder, "loading-run");
                yield return WaitFor(() => curtain.WetFill >= 0.45f, 10f);
                yield return Frame(folder, "loading-run-05");
                yield return WaitFor(() => curtain.EyebrowText == "ENTERING", 15f);
                yield return new WaitForSecondsRealtime(0.1f);
                yield return Frame(folder, "loading-run-entering");
                PrototypeUiFlow.ArtificialLoadSeconds = 0f;
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Run, 30f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                yield return new WaitForSecondsRealtime(1f);

                // Pause abort back to the menu: SCREEN, far plate, MAIN MENU, no tip. A short
                // artificial load (0.6 s) reaches 0.9 before the minimum display ends: the ensō
                // holds its mouth open until the stamp (the "fast load" state, §15.3).
                UiLoadTiming.RecordLoad(PrototypeScenes.MainMenu, 5f);
                PrototypeUiFlow.ArtificialLoadSeconds = 0.6f;
                PrototypeUiFlow.AbortRunToMainMenu();
                yield return WaitFor(() => curtain.GroupVisible && curtain.EyebrowText == "LOADING", 10f);
                yield return new WaitForSecondsRealtime(0.1f);
                yield return Frame(folder, "loading-abort");
                yield return WaitFor(() => curtain.WetFill >= UiCurtain.LoadShare - 0.005f || curtain.EyebrowText == "ENTERING", 10f);
                yield return new WaitForSecondsRealtime(0.1f);
                yield return Frame(folder, "loading-abort-09");
                PrototypeUiFlow.ArtificialLoadSeconds = 0f;
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.MainMenu, 30f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                yield return new WaitForSecondsRealtime(0.5f);

                // A Results-bound load that stalls (shown directly: a real stall needs a stuck load):
                // no tip (§15.1), the 10 s notice, then the ≥ 60 s error modal.
                curtain.Close(true);
                curtain.ShowGroup(UiCurtain.DestinationFor(PrototypeScenes.Results), string.Empty, null, false);
                curtain.SetProgress(0.45f);
                curtain.ShowStalled();
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Frame(folder, "loading-stalled");
                curtain.ShowError(PrototypeScenes.Results, true, null, null);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Frame(folder, "loading-error");
                curtain.HideError();
                curtain.Open(true);
                yield return null;

                // The ring alone (Ø 160) - the device the ink variant carries (10b-2).
                curtain.Close(true);
                curtain.ShowRingOnly();
                curtain.SetProgress(0.5f);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Frame(folder, "loading-ring-only");
                curtain.Open(true);
                yield return null;
                Debug.Log("[UiCapture] loading done · framebuffer " + Screen.width + "x" + Screen.height);
                Application.Quit();
            }

            /// <summary>
            /// 10b-2 · <c>-ddUiCapture boot</c>: the boot seal from the first frame - the ink blot
            /// (t 0.3), the stamp's contact (t 0.64), the wordmark (t 1.3), then the hand-off to
            /// LOADING (an artificial 3 s load keeps the menu from being ready at t 1.6).
            /// <c>-ddUiCaptureRm 1</c>: the static composite.
            /// </summary>
            private IEnumerator BootFrames()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                PrototypeUiFlow.ArtificialLoadSeconds = 3f;
                yield return WaitFor(() => UiCurtain.Instance != null && UiCurtain.Instance.BootVisible, 10f);
                float t0 = Time.realtimeSinceStartup;
                UiCurtain curtain = UiCurtain.Instance;
                yield return WaitFor(() => Time.realtimeSinceStartup - t0 >= 0.3f, 2f);
                yield return Frame(folder, "boot-blot");
                yield return WaitFor(() => Time.realtimeSinceStartup - t0 >= UiCurtain.BootStampAt + UiPresets.StampContactSeconds, 2f);
                yield return Frame(folder, "boot-stamp");
                yield return WaitFor(() => Time.realtimeSinceStartup - t0 >= 1.3f, 2f);
                yield return Frame(folder, "boot-wordmark");
                yield return WaitFor(() => curtain.EyebrowText == "LOADING" && Time.realtimeSinceStartup - t0 >= PrototypeUiFlow.BootHoldSeconds + 0.4f, 6f);
                yield return Frame(folder, "boot-handoff");
                yield return WaitFor(() => curtain.EyebrowText == "ENTERING", 10f);
                yield return new WaitForSecondsRealtime(0.1f);
                yield return Frame(folder, "boot-entering");
                PrototypeUiFlow.ArtificialLoadSeconds = 0f;
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.MainMenu, 20f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Frame(folder, "boot-menu");
                Debug.Log("[UiCapture] boot done · framebuffer " + Screen.width + "x" + Screen.height);
                Application.Quit();
            }

            /// <summary>
            /// 10b-2 · <c>-ddUiCapture ink</c>: a real run ended by death - the ink bleeding
            /// (t 1.0 of the beat), covered (t 2.2), the Ø 160 ring in the ink (the Results load
            /// stretched to 3 s), the recede half way, and Results.
            /// </summary>
            private IEnumerator InkFrames()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                if (Arg("-ddUiCaptureRm") == "1")
                {
                    UiMotion.ReducedMotion = true;
                }

                PrototypeUiFlow.GoToSelection();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Selection, 20f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                PrototypeUiFlow.StartRun();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Run, 30f);
                yield return UiCaptureAgent.WaitRunLive(20f);
                yield return new WaitForSecondsRealtime(2f);

                PrototypeUiFlow.ArtificialLoadSeconds = 3f;
                UiCurtain curtain = UiCurtain.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                if (rig != null && rig.Health != null)
                {
                    rig.Health.DiagnosisInvulnerable = false;
                    rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "ui-capture");
                }

                float t0 = Time.realtimeSinceStartup;
                yield return WaitFor(() => Time.realtimeSinceStartup - t0 >= 1.0f, 3f);
                yield return Frame(folder, "ink-bleed");
                yield return WaitFor(() => Time.realtimeSinceStartup - t0 >= 2.2f, 3f);
                yield return Frame(folder, "ink-covered");
                yield return WaitFor(() => curtain.RingOnlyVisible, 8f);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Frame(folder, "ink-ring");
                // AD 10b-2 wanted: ENTERING at 360°, the frame before the recede starts.
                yield return WaitFor(() => curtain.EyebrowText == "ENTERING", 20f);
                yield return WaitFor(() => curtain.RingFill >= 0.999f || curtain.InkReceding, 2f);
                yield return Frame(folder, "ink-entering");
                yield return WaitFor(() => SceneManager.GetActiveScene().name == PrototypeScenes.Results && !UiCurtain.IsCovering, 20f);
                yield return Frame(folder, "ink-recede");
                PrototypeUiFlow.ArtificialLoadSeconds = 0f;
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Frame(folder, "ink-results");
                Debug.Log("[UiCapture] ink done · framebuffer " + Screen.width + "x" + Screen.height);
                Application.Quit();
            }

            /// <summary>
            /// WP-068 slice 8 (<c>-ddUiCapture hudstates</c>): the HUD v3 panels in the states a fight reaches, staged on a
            /// live run - idle, a gold gain (the "+N" mid-rise, the leaf glint), low ammo, empty (EMPTY + the reload
            /// prompt), mid-reload (the ring), low health (LOW, coral frame, cracked heart). -ddUiScheme pad for the pad glyph.
            /// </summary>
            private IEnumerator HudStates()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                PrototypeUiFlow.GoToSelection();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Selection, 20f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                PrototypeUiFlow.StartRun();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Run, 30f);
                yield return UiCaptureAgent.WaitRunLive(20f);

                // 8-R: staged states, not a fight - the ronin takes damage only where a stage deals it (the plaster run
                // lost the player to zombies before its low-health frame).
                PlayerRig guard = PlayerRegistry.Primary;
                if (guard != null && guard.Health != null)
                {
                    guard.Health.DiagnosisInvulnerable = true;
                }

                // AD 8b MUST-1: -ddUiStage plaster | pond puts the player where the pale plates sit behind the HUD panels
                // (AP-005 placement: the palace's plaster screens at z ≈ 33, the koi pond at z −26 … −37).
                PlayerRig rig = PlayerRegistry.Primary;
                string stage = Arg("-ddUiStage");
                if (rig != null && (stage == "plaster" || stage == "plaster-south" || stage == "pond"))
                {
                    // plaster-south (AD 8-R · A SHOULD-5): north of the screens, so they sit under the bottom clusters.
                    float y = rig.transform.position.y;
                    Vector3 at = stage == "plaster" ? new Vector3(3f, y, 28f) : stage == "plaster-south" ? new Vector3(3f, y, 41f) : new Vector3(0f, y, -21f);
                    rig.Teleport(at, rig.transform.rotation);
                }

                yield return new WaitForSecondsRealtime(1.5f);
                yield return Frame(folder, "hud-idle");
                yield return UiCaptureAgent.Coverage(folder, "hud-idle");

                // AD 8b SHOULD-1: the reticle's states, staged on the live reticle.
                ClassicWeaponHudState live = WeaponEvents.Classic;
                yield return StageReticle(folder, "reticle-saturated", live, 9f, 0f, 0, live.AmmoInMagazine, false);
                yield return StageReticle(folder, "reticle-empty", live, live.CurrentSpreadDegrees, 0f, 0, 0, false);
                yield return StageReticle(folder, "reticle-pellet", live, 7.5f, 4.5f, 8, live.AmmoInMagazine, false);
                yield return StageReticle(folder, "reticle-special", live, live.CurrentSpreadDegrees, 0f, 0, live.AmmoInMagazine, true);
                bool reduced = UiMotion.ReducedMotion;
                UiMotion.ReducedMotion = true;
                yield return StageReticle(folder, "reticle-rm", live, 3.5f, 0f, 0, live.AmmoInMagazine, false);
                UiMotion.ReducedMotion = reduced;
                RunHudScreen.CaptureWeapon = null;
                RunHudScreen.CaptureSpecialActive = false;

                // AD 8b MUST-2: a rear attack landing (the wedge inside the hairline), then a hit from another side (a wedge alone).
                RunHudScreen hud = FindObjectOfType<RunHudScreen>();
                if (hud != null)
                {
                    RunHudScreen.CaptureRearDirection = Vector3.back;
                    yield return new WaitForSecondsRealtime(0.3f);
                    hud.CaptureHit(Vector3.back);
                    yield return new WaitForSecondsRealtime(0.05f);
                    yield return Frame(folder, "hud-arcs-rear-hit");
                    yield return new WaitForSecondsRealtime(0.6f);
                    hud.CaptureHit(Vector3.right);
                    yield return new WaitForSecondsRealtime(0.05f);
                    yield return Frame(folder, "hud-arcs-other-hit");
                    RunHudScreen.CaptureRearDirection = null;
                    yield return new WaitForSecondsRealtime(0.3f);

                    // 8-R (AD pre-evaluation, user set): two threat chips side by side with the rear arc, staged.
                    RunHudScreen.CaptureThreats = true;
                    RunHudScreen.CaptureRearDirection = Vector3.back;
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Frame(folder, "hud-threat");

                    // M4-R1 N2 (AD R1-A MUST-1d): third / first person - the rear-edge arc at 120°, 180° and 240° from the
                    // camera's heading (all outside the view), the stroke on the inset, clear of the clusters.
                    if (DD.Prototype.CameraSystem.CameraModeService.IsLookMode && Camera.main != null)
                    {
                        foreach (int bearing in RearBearings)
                        {
                            RunHudScreen.CaptureRearDirection = RearFromCamera(bearing);
                            yield return new WaitForSecondsRealtime(0.4f);
                            yield return Frame(folder, "hud-rear-" + bearing);
                        }
                    }

                    RunHudScreen.CaptureThreats = false;
                    RunHudScreen.CaptureRearDirection = null;
                    yield return new WaitForSecondsRealtime(1.2f);   // the chips' 800 / 300 ms hysteresis

                    // 8-R (AD SHOULD-5): the depth pair - the forms' back planes at the far left / right of their drift,
                    // the numbers where they are.
                    RunHudScreen.CaptureParallax = new Vector2(-3f, 1f);
                    yield return new WaitForSecondsRealtime(0.15f);
                    yield return Frame(folder, "hud-parallax-left");
                    RunHudScreen.CaptureParallax = new Vector2(3f, -1f);
                    yield return new WaitForSecondsRealtime(0.15f);
                    yield return Frame(folder, "hud-parallax-right");
                    RunHudScreen.CaptureParallax = null;
                }

                if (GoldService.Instance != null && rig != null)
                {
                    GoldService.Instance.Wallet.Credit(25, rig.transform.position);
                    yield return new WaitForSecondsRealtime(0.3f); // AD 8a SHOULD-5: the glint mid-sweep (t 0.3 of 0.6 s)
                    yield return Frame(folder, "hud-gold-gain");
                }

                WeaponController weapon = WeaponRegistry.ActiveController;
                ClassicWeaponRuntime classic = weapon != null ? weapon.Classic : null;
                if (classic != null)
                {
                    int low = Mathf.Max(1, Mathf.FloorToInt(classic.MagazineSize * RunHudViewModel.LowAmmoFraction));
                    while (classic.Ammo > low)
                    {
                        classic.ConsumeShot();
                    }

                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Frame(folder, "hud-ammo-low");

                    while (classic.Ammo > 0)
                    {
                        classic.ConsumeShot();
                    }

                    // The HUD reads the weapon's published snapshot, a frame behind the runtime.
                    yield return null;
                    yield return null;
                    yield return new WaitForSecondsRealtime(0.2f);
                    yield return Frame(folder, "hud-empty");

                    classic.TryBeginReload();
                    yield return WaitFor(() => classic.ReloadProgress01 >= 0.45f, 5f);
                    yield return Frame(folder, "hud-reload");
                    yield return WaitFor(() => !classic.IsReloading, 5f);
                    yield return new WaitForSecondsRealtime(0.05f);
                    yield return Frame(folder, "hud-reloaded");
                }

                if (rig != null && rig.Health != null)
                {
                    rig.Health.DiagnosisInvulnerable = false;
                    rig.Health.ApplyDamage(rig.Health.Max * 0.8f, rig.transform.position + Vector3.forward, "ui-capture");
                    rig.Health.DiagnosisInvulnerable = true;
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Frame(folder, "hud-low-health");
                    yield return UiCaptureAgent.Coverage(folder, "hud-low-health");
                }

                yield return SpecialStates(folder, rig);

                // WP-068 8c · E22: a real death - the health block held at α 1 with the line, then dimmed with the rest.
                if (rig != null && rig.Health != null)
                {
                    rig.Health.DiagnosisInvulnerable = false;
                    rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "Zombie.Standard.Swipe");
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Frame(folder, "hud-death");
                    yield return new WaitForSecondsRealtime(0.9f);
                    yield return Frame(folder, "hud-death-dim");
                }

                Debug.Log("[UiCapture] hudstates done · framebuffer " + Screen.width + "x" + Screen.height);
                Application.Quit();
            }

            /// <summary>
            /// WP-068 8c · the Special's HUD on a real drop (SpecialWeaponDirector.ForceSpawn):
            /// - the diamond in view; then, from the staging spot farther from the drop, the EdgeMarker;
            /// - the pickup, walked onto (ring filling, sparks, banner mid-wipe), then settled with its ticks;
            /// - status stamps on two bodies in view (a web and a push);
            /// - the final seconds (coral ring); then the return (the banner wiping again, the classic block un-dimmed).
            /// </summary>
            private static IEnumerator SpecialStates(string folder, PlayerRig rig)
            {
                // AD 8c SHOULD-3: NET and DAZED side by side, while the bodies are still around the ronin (before the drop).
                int stamped = 0;
                Camera camera = Camera.main;
                System.Collections.Generic.IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = 0; camera != null && live != null && i < live.Count && stamped < 2; i++)
                {
                    ZombieEnemy z = live[i];
                    if (z == null || !z.IsAlive)
                    {
                        continue;
                    }

                    Vector3 v = camera.WorldToViewportPoint(z.transform.position);
                    if (v.z < 0f || v.x < 0.15f || v.x > 0.85f || v.y < 0.2f || v.y > 0.8f)
                    {
                        continue;
                    }

                    bool web = stamped == 0;
                    z.ApplySpecialReaction(new DD.Prototype.Damage.SpecialReaction(
                        web ? DD.Prototype.Damage.SpecialReactionKind.Web : DD.Prototype.Damage.SpecialReactionKind.Push,
                        web ? DD.Prototype.Core.Config.SpecialWeaponId.Namekuji : DD.Prototype.Core.Config.SpecialWeaponId.Raiju,   // WP-069 B4 interim (S-02)
                        Vector3.forward, web ? 0f : 1f, web ? 4f : 1.5f, 0));
                    stamped++;
                }

                if (stamped > 0)
                {
                    yield return new WaitForSecondsRealtime(0.04f);
                    yield return Frame(folder, "hud-status-stamp");
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Frame(folder, "hud-status");
                }


                SpecialWeaponDirector director = SpecialWeaponDirector.Instance;
                if (director == null || rig == null || !director.ForceSpawn())
                {
                    Debug.Log("[UiCapture] hudstates: no Special drop (director " + (director != null) + ")");
                    yield break;
                }

                yield return WaitFor(() => WeaponEvents.Special.Phase == SpecialWeaponPhase.OnGround, 3f);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Frame(folder, "hud-special-ground");

                Vector3 drop = WeaponEvents.Special.WorldPosition;
                float y = rig.transform.position.y;

                // AD 8c SHOULD-5: the drop near and in view - the diamond at its near size, the ground pip.
                rig.Teleport(new Vector3(drop.x, y, drop.z - 8f), rig.transform.rotation);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Frame(folder, "hud-marker-near");

                // AD 8c MUST-2: the edge marker at four bearings (the drop off each corner) - it must slide clear of the clusters.
                var corners = new[] { ("ne", -1f, -1f), ("nw", 1f, -1f), ("se", -1f, 1f), ("sw", 1f, 1f) };
                foreach (var corner in corners)
                {
                    rig.Teleport(new Vector3(drop.x + corner.Item2 * 26f, y, drop.z + corner.Item3 * 26f), rig.transform.rotation);
                    yield return new WaitForSecondsRealtime(0.5f);
                    yield return Frame(folder, "hud-marker-edge-" + corner.Item1);
                }

                // AD 8c SHOULD-5: the late-urgency marker - gold toward ember on the pickup's own clock.
                rig.Teleport(new Vector3(drop.x, y, drop.z - 8f), rig.transform.rotation);
                yield return WaitFor(() => WeaponEvents.Special.Phase != SpecialWeaponPhase.OnGround || WeaponEvents.Special.GroundProgress01 < 0.2f, 45f);
                if (WeaponEvents.Special.Phase == SpecialWeaponPhase.OnGround)
                {
                    yield return Frame(folder, "hud-marker-ember");
                }

                rig.Teleport(new Vector3(drop.x, y, drop.z), rig.transform.rotation);
                yield return WaitFor(() => WeaponEvents.Special.Phase == SpecialWeaponPhase.Active, 3f);
                yield return new WaitForSecondsRealtime(0.08f);
                yield return Frame(folder, "hud-special-pickup");
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Frame(folder, "hud-special-online");

                // AD R1-A MUST-1d: straight behind with the Special ring up - the arc lifts above the ring, x unchanged.
                if (DD.Prototype.CameraSystem.CameraModeService.IsLookMode && Camera.main != null)
                {
                    RunHudScreen.CaptureRearDirection = RearFromCamera(180);
                    RunHudScreen.CaptureThreats = true;
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Frame(folder, "hud-rear-180-special");
                    RunHudScreen.CaptureRearDirection = null;
                    RunHudScreen.CaptureThreats = false;
                }

                yield return WaitFor(() => WeaponEvents.Special.FinalSecondsWarning, 60f);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Frame(folder, "hud-special-final");
                yield return WaitFor(() => WeaponEvents.Special.Phase == SpecialWeaponPhase.None, 10f);
                yield return new WaitForSecondsRealtime(0.1f);
                yield return Frame(folder, "hud-banner-wipe");
                yield return new WaitForSecondsRealtime(0.7f);
                yield return Frame(folder, "hud-special-ended");
            }

            private static IEnumerator StageReticle(string folder, string name, ClassicWeaponHudState live, float spread, float pelletRing, int pellets, int ammo, bool special)
            {
                ClassicWeaponHudState state = live;
                state.CurrentSpreadDegrees = spread;
                state.PelletRingDegrees = pelletRing;
                state.PelletCount = pellets;
                state.AmmoInMagazine = ammo;
                RunHudScreen.CaptureWeapon = state;
                RunHudScreen.CaptureSpecialActive = special;
                yield return new WaitForSecondsRealtime(0.4f); // past the spread follow
                yield return Frame(folder, name);
            }

            private static IEnumerator WaitFor(Func<bool> condition, float timeout)
            {
                float t0 = Time.realtimeSinceStartup;
                while (!condition() && Time.realtimeSinceStartup - t0 < timeout)
                {
                    yield return null;
                }
            }

            private static IEnumerator Frame(string folder, string name)
            {
                yield return new WaitForEndOfFrame();
                WritePng(Path.Combine(folder, name + ".png"));
            }

            private static string Readout(string id)
            {
                return "\"id\": \"" + id + "\", \"mode\": \"" + Screen.fullScreenMode + "\", \"screen\": \"" + Screen.width + "x" + Screen.height + "\"";
            }

            private static readonly int[] RearBearings = { 120, 180, 240 };

            /// <summary>A world direction at <paramref name="bearing"/>° clockwise from the main camera's flat heading.</summary>
            private static Vector3 RearFromCamera(int bearing)
            {
                Vector3 forward = Vector3.ProjectOnPlane(Camera.main.transform.forward, Vector3.up).normalized;
                return Quaternion.Euler(0f, bearing, 0f) * forward;
            }

        }
    }
}
