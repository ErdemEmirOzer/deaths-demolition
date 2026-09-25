using System;
using System.Collections;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
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
    /// 12b: <c>menumatrix</c> (E3), <c>-ddUiKilledBy</c> (A8), <c>-ddUiCaptureWeapon</c> (B11), hudstates' reticle-floor and 4× crops (B7 / B11).
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

        /// <summary>
        /// 12b · A8 (AD R1-D SHOULD-5): <c>-ddUiKilledBy &lt;SourceId&gt;</c> names the killing blow of the staged
        /// <c>results</c> death (default "ui-capture"); <c>longest</c> is the longest id in EnemyAttackState's SourceId table
        /// (<see cref="LongestSourceId"/>, 24 characters - "Zombie.Standard.BiteGrab" ties it). With
        /// <c>-ddUiCaptureRounds 10</c> the death lands in round 11: the results-mac frame of the H1 / ribbon rule.
        /// </summary>
        private const string KilledByFlag = "-ddUiKilledBy";
        public const string LongestSourceId = "Zombie.Standard.Overhead";

        /// <summary>
        /// 12b · B11 (AD R1-B SHOULD-5): <c>-ddUiCaptureWeapon &lt;ClassicWeaponId&gt;</c> (e.g. <c>Sg2</c>, the Nami
        /// shotgun) starts the staged run with that classic weapon instead of the selection's - a capture seam, it does
        /// not ask the profile whether the weapon is unlocked.
        /// </summary>
        private const string WeaponFlag = "-ddUiCaptureWeapon";

        private static string CaptureKilledBy()
        {
            string id = Arg(KilledByFlag);
            if (string.IsNullOrEmpty(id))
            {
                return "ui-capture";
            }

            return string.Equals(id, "longest", StringComparison.OrdinalIgnoreCase) ? LongestSourceId : id;
        }

        private static void ApplyCaptureWeapon()
        {
            string name = Arg(WeaponFlag);
            ClassicWeaponId id;
            if (!string.IsNullOrEmpty(name) && Enum.TryParse(name, true, out id))
            {
                PrototypeUiFlow.SelectedWeapon = id;
                Debug.Log("[UiCapture] classic weapon " + id);
            }
            else if (!string.IsNullOrEmpty(name))
            {
                Debug.LogWarning("[UiCapture] " + WeaponFlag + " " + name + ": not a ClassicWeaponId");
            }
        }

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

        /// <summary>Capture seam: a fixed weapon roll, every range from the shipped source.</summary>
        private sealed class ForcedRoll : ISpecialWeaponRandom
        {
            private readonly float _value;

            public ForcedRoll(float value)
            {
                _value = value;
            }

            public float Value() => _value;

            public float Range(float min, float max) => UnityRandomSource.Instance.Range(min, max);

            public int Range(int min, int maxExclusive) => UnityRandomSource.Instance.Range(min, maxExclusive);
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

                if (_screen == "menumatrix")
                {
                    yield return MenuMatrix();
                    yield break;
                }

                if (_screen == "tree")
                {
                    yield return TreeFrames();
                    Debug.Log("[UiCapture] tree done");
                    Application.Quit();
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
                        ApplyCaptureWeapon();
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
                            // AD R1-D MUST-1 evidence: -ddUiCaptureRounds N completes N rounds first (the real round-end
                            // flow: XP, a level up on a fresh sandbox profile, gold) so Results shows earned values.
                            int rounds;
                            if (int.TryParse(Arg("-ddUiCaptureRounds"), out rounds) && rounds > 0 && RunDirector.Instance != null)
                            {
                                PlayerRig guard = PlayerRegistry.Primary;
                                if (guard != null && guard.Health != null)
                                {
                                    guard.Health.DiagnosisInvulnerable = true;
                                }

                                for (int r = 0; r < rounds; r++)
                                {
                                    yield return WaitFor(() => RunDirector.Instance.Phase == RunPhase.Fighting, 40f);
                                    yield return new WaitForSecondsRealtime(0.5f);
                                    DD.Prototype.Spawn.EnemySpawnDirector spawn = DD.Prototype.Spawn.EnemySpawnDirector.Instance;
                                    if (spawn == null || !spawn.DebugCompleteRound())
                                    {
                                        break;
                                    }

                                    yield return WaitFor(() => RunDirector.Instance.Phase == RunPhase.Upgrading, 30f);
                                    RunDirector.Instance.CloseUpgradeScreen();
                                }

                                yield return WaitFor(() => RunDirector.Instance.Phase == RunPhase.Fighting, 40f);
                                yield return new WaitForSecondsRealtime(1f);
                            }

                            PlayerRig rig = PlayerRegistry.Primary;
                            if (rig != null && rig.Health != null)
                            {
                                rig.Health.DiagnosisInvulnerable = false;
                                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, CaptureKilledBy());   // 12b · A8
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

                // SCREEN depth into the run: key art, weapon sub-line, tip; the ensō at 0 / 0.5 /
                // 0.9 (an artificial 5 s load, §15.7), the push-in at t 0 / t 4 s (12b, AD R1-D SHOULD-8:
                // scale logged, the edge moves (scale - 1) × half the width) and the seal's stamp at ENTERING (10b-2).
                UiLoadTiming.RecordLoad(PrototypeScenes.Run, 5f);
                PrototypeUiFlow.ArtificialLoadSeconds = 5f;
                PrototypeUiFlow.StartRun();
                yield return WaitFor(() => curtain.GroupVisible && curtain.EyebrowText == "LOADING", 10f);
                float groupAt = Time.realtimeSinceStartup;
                yield return Frame(folder, "loading-run-t0");
                Debug.Log("[UiCapture] loading-run-t0 keyArtScale=" + curtain.KeyArtScale.ToString("F4") + " t=" + (Time.realtimeSinceStartup - groupAt).ToString("F2"));
                yield return new WaitForSecondsRealtime(0.25f);
                yield return Frame(folder, "loading-run");
                yield return WaitFor(() => curtain.WetFill >= 0.45f, 10f);
                yield return Frame(folder, "loading-run-05");
                yield return WaitFor(() => Time.realtimeSinceStartup - groupAt >= 4f, 10f);
                yield return Frame(folder, "loading-run-t4");
                Debug.Log("[UiCapture] loading-run-t4 keyArtScale=" + curtain.KeyArtScale.ToString("F4") + " t=" + (Time.realtimeSinceStartup - groupAt).ToString("F2")
                          + " edge=" + ((curtain.KeyArtScale - 1f) * Screen.width * 0.5f).ToString("F1") + "px");
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
                curtain.ShowGroup(UiCurtain.DestinationFor(PrototypeScenes.Results), string.Empty, null, false, UiCurtain.KeyArtFor(PrototypeScenes.Results));
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
            /// (t 1.0 of the beat), covered (t 2.2: what a fast load shows before Results), the Results key art opened
            /// inside the ink with the ring Ø 240 (12b decision A; the Results load stretched to 3 s), the recede half way,
            /// and Results.
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
                Debug.Log("[UiCapture] ink-covered done at +" + (Time.realtimeSinceStartup - t0).ToString("F2") + " s after the death");
                yield return WaitFor(() => curtain.GroupVisible && curtain.KeyArtVisible && curtain.InkCovering, 8f);
                yield return new WaitForSecondsRealtime(0.8f);
                Debug.Log("[UiCapture] ink-keyart · " + curtain.DebugState);
                yield return Frame(folder, "ink-keyart");
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
                ApplyCaptureWeapon();   // 12b · B11: -ddUiCaptureWeapon Sg2 - the Nami glyph at idle and LOW
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
                yield return Crop(folder, "crop-glyph-idle-x4", "WeaponIcon", 12f);   // 12b · B11 (AD R1-B SHOULD-5): the glyph at idle
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

                // 12b · B7 (AD R1-A SHOULD-5): the spread floor (CurrentSpreadDegrees 0) - TP ring 14 px, FP 11.2 px - with a
                // 4× centre crop; then the same with CENTRE DOT on (dot 4 / 3 px): the FP inner tick ends must keep 4 px from it.
                yield return StageReticle(folder, "reticle-floor", live, 0f, 0f, 0, live.AmmoInMagazine, false);
                yield return Crop(folder, "reticle-floor-x4", "Reticle", 40f);
                bool dot = UiSettings.CentreDot;
                UiSettings.CentreDot = true;
                yield return StageReticle(folder, "reticle-floor-dot", live, 0f, 0f, 0, live.AmmoInMagazine, false);
                yield return Crop(folder, "reticle-floor-dot-x4", "Reticle", 40f);
                UiSettings.CentreDot = dot;
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
                    yield return Crop(folder, "crop-glyph-low-x4", "WeaponIcon", 12f);   // 12b · B11: the glyph at LOW (α 0.70)

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
                    yield return Crop(folder, "crop-reload-ring-x4", "ReloadRing", 8f);   // 12b · B11: the Focus400 ring mid-reload
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

                    // 12c (AD 12b SHOULD-3, B10): ammo and health both LOW - "LOW AMMO" over "LOW" in the one column.
                    if (classic != null)
                    {
                        int lowAmmo = Mathf.Max(1, Mathf.FloorToInt(classic.MagazineSize * RunHudViewModel.LowAmmoFraction));
                        while (classic.Ammo > lowAmmo)
                        {
                            classic.ConsumeShot();
                        }

                        yield return new WaitForSecondsRealtime(0.3f);
                        yield return Frame(folder, "hud-both-low");
                    }
                }

                yield return StampCollisions(folder, rig);

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
            /// 12c (AD 12b SHOULD-2, B1 / B2): status stamps staged where their true point collides - a zombie is placed so its
            /// stamp anchor (<see cref="UiEnemyStatusMarkers.HeadHeight"/> over its feet) projects onto the target, then shocked:
            /// - <c>stamp-banner</c>: inside the banner's box, with the banner up (the stamp sorts under it);
            /// - <c>stamp-figure</c> (third person): on the player figure (it lifts to the figure + 24);
            /// - <c>stamp-cluster</c>: inside the bottom-left ink cluster (it leaves toward the middle, + 12).
            /// Each frame logs the target and where the stamp resolved.
            /// </summary>
            private static IEnumerator StampCollisions(string folder, PlayerRig rig)
            {
                RunHudScreen hud = UnityEngine.Object.FindObjectOfType<RunHudScreen>();
                Camera camera = Camera.main;
                if (hud == null || camera == null || rig == null)
                {
                    yield break;
                }

                var clusters = new Rect[8];
                int n = hud.StatusStampAvoidRects(clusters);
                Rect corner = default(Rect);
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (clusters[i].center.x + clusters[i].center.y < best)
                    {
                        best = clusters[i].center.x + clusters[i].center.y;
                        corner = clusters[i];
                    }
                }

                Rect figure;
                bool thirdPerson = hud.PlayerFigureRect(out figure);
                Vector2 canvas = ((RectTransform)hud.transform).rect.size;
                int id = 990100;
                for (int step = 0; step < 3; step++)
                {
                    string name = step == 0 ? "stamp-banner" : step == 1 ? "stamp-figure" : "stamp-cluster";
                    if (step == 1 && !thirdPerson)
                    {
                        continue;
                    }

                    Rect target = step == 0 ? hud.CaptureBanner("ROUND 4") : step == 1 ? figure : corner;
                    if (step == 2 && n == 0)
                    {
                        continue;
                    }

                    Vector2 point = step == 1 ? new Vector2(target.center.x, target.yMin + target.height * 0.6f) : target.center;
                    var viewport = new Vector3(point.x / canvas.x + 0.5f, point.y / canvas.y + 0.5f, 0f);
                    ZombieEnemy z = PlaceUnder(camera, viewport, rig);
                    if (z == null)
                    {
                        Debug.Log("[UiCapture] " + name + ": no live zombie could be placed under " + point);
                        continue;
                    }

                    z.ApplyShock(id++, 4f);
                    yield return new WaitForSecondsRealtime(0.12f);
                    yield return Frame(folder, name);
                    Debug.Log("[UiCapture] " + name + " target=" + target + " point=" + point + " stamp=" + UiEnemyStatusMarkers.LastPlaced);
                    yield return new WaitForSecondsRealtime(0.5f);
                }
            }

            /// <summary>Moves the live zombie nearest the player so its stamp anchor projects to <paramref name="viewport"/>.</summary>
            private static ZombieEnemy PlaceUnder(Camera camera, Vector3 viewport, PlayerRig rig)
            {
                System.Collections.Generic.IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                ZombieEnemy pick = null;
                float nearest = float.MaxValue;
                for (int i = 0; live != null && i < live.Count; i++)
                {
                    ZombieEnemy z = live[i];
                    float d = z != null && z.IsAlive ? (z.transform.position - rig.transform.position).sqrMagnitude : float.MaxValue;
                    if (d < nearest)
                    {
                        nearest = d;
                        pick = z;
                    }
                }

                if (pick == null)
                {
                    return null;
                }

                Ray ray = camera.ViewportPointToRay(viewport);
                float anchorY = pick.transform.position.y + UiEnemyStatusMarkers.HeadHeight;
                if (Mathf.Abs(ray.direction.y) < 1e-4f)
                {
                    return null;
                }

                float t = (anchorY - ray.origin.y) / ray.direction.y;
                if (t <= 0.5f || t > 80f)
                {
                    return null;   // the anchor plane is behind the camera or past the playable ground
                }

                Vector3 anchor = ray.GetPoint(t);
                var controller = pick.GetComponent<CharacterController>();
                if (controller != null)
                {
                    controller.enabled = false;
                }

                pick.transform.position = new Vector3(anchor.x, pick.transform.position.y, anchor.z);
                if (controller != null)
                {
                    controller.enabled = true;
                }

                return pick;
            }

            /// <summary>
            /// WP-068 8c · the Special's HUD on a real drop (SpecialWeaponDirector.ForceSpawn):
            /// - the diamond in view; then, from the staging spot farther from the drop, the EdgeMarker;
            /// - the pickup, walked onto (ring filling, sparks, banner mid-wipe), then settled with its ticks;
            /// - status stamps on two bodies in view (SHOCK and DOWN: Raiju's hold and a gel knockdown, M4-R1 N19);
            /// - the final seconds (coral ring); then the return (the banner wiping again, the classic block un-dimmed).
            /// </summary>
            private static IEnumerator SpecialStates(string folder, PlayerRig rig)
            {
                // AD 8c SHOULD-3 (N19 v2): SHOCK and DOWN side by side, while the bodies are still around the ronin (before the drop).
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

                    bool shock = stamped == 0;
                    bool applied = shock ? z.ApplyShock(990001, 4f) : z.ApplyKnockdown(990002, 4f);
                    stamped += applied ? 1 : 0;
                }

                if (stamped > 0)
                {
                    yield return new WaitForSecondsRealtime(0.04f);
                    yield return Frame(folder, "hud-status-stamp");
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Frame(folder, "hud-status");
                }


                SpecialWeaponDirector director = SpecialWeaponDirector.Instance;

                // -ddUiCaptureSpecial raiju | namekuji: which drop this frame set shows (the weapon roll is the director's only
                // Value() call, so a fixed value picks it; drop positions still come from the shipped source).
                string forced = Arg("-ddUiCaptureSpecial");
                if (director != null && !string.IsNullOrEmpty(forced))
                {
                    director.SetRandomSource(new ForcedRoll(forced == "namekuji" ? 0.9999f : 0f));
                }

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

                // AD R1-B MUST-2b: the usage row staged at every size ("SHOTS 2 · HITS 3") - two shots, three bodies reached.
                DD.Prototype.Core.Config.SpecialWeaponId inHand = WeaponEvents.Special.WeaponId;
                SpecialWeaponUsageEvents.Publish(inHand, SpecialUsageKind.Fired, "capture", Vector3.zero, Vector3.forward, 0f, 0f, 0);
                SpecialWeaponUsageEvents.Publish(inHand, SpecialUsageKind.Fired, "capture", Vector3.zero, Vector3.forward, 0f, 0f, 0);
                for (int hit = 0; hit < 3; hit++)
                {
                    SpecialWeaponUsageEvents.Publish(inHand, SpecialUsageKind.Hit, "capture", Vector3.zero, Vector3.forward, 0f, 0f, 1);
                }

                yield return new WaitForSecondsRealtime(0.2f);
                yield return Frame(folder, "hud-special-online");

                // E14 v3 (DEC-097): the reload refilling the ring and Raiju's charge arc, staged on the live snapshot (the
                // runtime republishes it every frame, so the staged state is re-asserted until the frame is taken).
                yield return StagedSpecial(folder, "hud-special-reload", s => { s.IsReloading = true; s.ReloadProgress01 = 0.45f; s.AmmoInMagazine = 0; return s; });
                yield return StagedSpecial(folder, "hud-special-charge", s => { s.Charge01 = 0.6f; return s; });

                // N19 §2: the SLICK hint in the E23 slot, docked above the Special block while it shows.
                RunHudScreen.CaptureSlick = true;
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Frame(folder, "hud-slick-special");

                // AD R1-B MUST-3: in a look mode, straight behind with the Special and SLICK stacked - the arc clears both.
                if (DD.Prototype.CameraSystem.CameraModeService.IsLookMode && Camera.main != null)
                {
                    RunHudScreen.CaptureRearDirection = RearFromCamera(180);
                    RunHudScreen.CaptureThreats = true;
                    yield return new WaitForSecondsRealtime(0.4f);
                    yield return Frame(folder, "hud-rear-180-slick");
                    RunHudScreen.CaptureRearDirection = null;
                    RunHudScreen.CaptureThreats = false;
                }

                RunHudScreen.CaptureSlick = false;

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

                RunHudScreen.CaptureSlick = true;
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Frame(folder, "hud-slick");
                RunHudScreen.CaptureSlick = false;
            }

            /// <summary>
            /// WP-068 R1-C: <c>-ddUiCapture tree</c> - the in-run tree on the round-end shop with node cards on (WP-077
            /// K10a), over four staged shops: round 1 mid Opening Beat, open, a focused offer, a purchase with UNDO LAST;
            /// rounds 2-4 buy the offer (keystones and forks first) and stoke the Hearth once it opens, so OWNED, CLOSED BY,
            /// the Hearth and a pinned path all reach a frame. The player is invulnerable; the rounds are completed by the
            /// spawn director's debug path, so every shop is the real round-end flow.
            /// </summary>
            private IEnumerator TreeFrames()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                PrototypeUiFlow.GoToSelection();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Selection, 20f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                PrototypeUiFlow.StartRun();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Run, 30f);
                yield return UiCaptureAgent.WaitRunLive(20f);
                PlayerRig rig = PlayerRegistry.Primary;
                if (rig != null && rig.Health != null)
                {
                    rig.Health.DiagnosisInvulnerable = true;
                }

                RunDirector run = RunDirector.Instance;
                if (run == null || run.Shop == null || run.Wallet == null)
                {
                    Debug.LogWarning("[UiCapture] tree: no run");
                    yield break;
                }

                run.Shop.NodeCardsOverride = true;
                run.Wallet.DebugSet(3000);

                // Capture only: S-04's effect seam (RunTreeEffects.TreatAllAsLive, as NodeCardTests) opens the keystones,
                // forks and capstones whose effects are still pending in this build, so the frames show the designed states.
                RunTreeEffects.TreatAllAsLive = true;
                bool hearthShot = false;
                for (int round = 1; round <= 5; round++)
                {
                    yield return WaitFor(() => run.Phase == RunPhase.Fighting, 40f);
                    yield return new WaitForSecondsRealtime(0.5f);
                    DD.Prototype.Spawn.EnemySpawnDirector spawn = DD.Prototype.Spawn.EnemySpawnDirector.Instance;
                    if (spawn == null || !spawn.DebugCompleteRound())
                    {
                        Debug.LogWarning("[UiCapture] tree: round " + round + " could not be completed");
                        yield break;
                    }

                    yield return WaitFor(() => run.Phase == RunPhase.Upgrading, 30f);
                    UiRunTreePanel tree = null;
                    yield return WaitFor(() => (tree = FindObjectOfType<UiRunTreePanel>()) != null, 5f);
                    if (tree == null)
                    {
                        Debug.LogWarning("[UiCapture] tree: no tree panel in round " + round);
                        yield break;
                    }

                    if (round == 5)
                    {
                        yield return TreeStates(folder, run, tree);
                        run.CloseUpgradeScreen();
                        break;
                    }

                    if (round == 1)
                    {
                        yield return new WaitForSecondsRealtime(0.2f);
                        yield return Frame(folder, "tree-r1-opening");
                        yield return new WaitForSecondsRealtime(1.2f);
                        yield return Frame(folder, "tree-r1-open");
                    }

                    for (int pick = 0; pick < 2; pick++)
                    {
                        int node = BestOffered(tree.Snapshot);
                        if (node < 0 || run.Shop.IsLocked)
                        {
                            break;
                        }

                        tree.FocusNode(node);
                        UiInput.Focus(tree.BuyButton);
                        yield return new WaitForSecondsRealtime(0.35f);
                        if (round == 1 && pick == 0)
                        {
                            yield return Frame(folder, "tree-r1-focus-offer");
                        }

                        tree.OnBuy();
                        yield return new WaitForSecondsRealtime(0.2f);
                        if (round == 1 && pick == 0)
                        {
                            yield return Frame(folder, "tree-r1-bought-undo");
                        }
                    }

                    int hearth = tree.Snapshot.HearthIndex;
                    if (hearth >= 0 && tree.Snapshot.Nodes[hearth].Status == RunTreeNodeStatus.Available)
                    {
                        tree.FocusNode(hearth);
                        tree.OnBuy();
                        yield return new WaitForSecondsRealtime(0.35f);
                        if (!hearthShot)
                        {
                            hearthShot = true;
                            yield return Frame(folder, "tree-hearth-stoked");
                        }
                    }

                    if (round == 4)
                    {
                        tree.SetLane(RunTreeLane.Weapon, false);
                        int pin = PinTarget(tree.Snapshot);
                        if (pin >= 0)
                        {
                            tree.FocusNode(pin);
                            tree.OnPin();
                        }

                        yield return new WaitForSecondsRealtime(0.4f);
                        yield return Frame(folder, "tree-r4-weapon-pinned");
                        int closed = FirstWith(tree.Snapshot, RunTreeNodeStatus.Excluded);
                        if (closed >= 0)
                        {
                            tree.FocusNode(closed);
                            yield return new WaitForSecondsRealtime(0.3f);
                            yield return Frame(folder, "tree-r4-closed");
                        }

                        tree.SetLane(RunTreeLane.Body, false);
                        yield return new WaitForSecondsRealtime(0.4f);
                        yield return Frame(folder, "tree-r4-body");
                    }

                    run.CloseUpgradeScreen();
                }

                RunTreeEffects.TreatAllAsLive = false;
            }

            /// <summary>
            /// AD R1-C MUST-5 / SHOULD-6: the states four shops of real play rarely line up, staged on the round-5 shop - a
            /// weapon keystone owned (the gate opens, forks turn NEW, the Hearth opens), one fork side owned (its partner
            /// CLOSED BY, REFORGE from round 5), one tier at its maximum (MAX), and 70 gold left (N GOLD SHORT on the dearer
            /// nodes). Capture only: the tree state is written directly (RunTreeState.MarkOwned, the build's SetTier).
            /// </summary>
            private static IEnumerator TreeStates(string folder, RunDirector run, UiRunTreePanel tree)
            {
                RoundUpgradeShop shop = run.Shop;
                RunTreeSnapshot snap = tree.Snapshot;
                tree.SetLane(RunTreeLane.Weapon, false);
                int keystone = First(snap, RunTreeLane.Weapon, RunTreeNodeKind.BranchKeystone, false);
                if (keystone >= 0)
                {
                    shop.Tree.MarkOwned(keystone);
                }

                int fork = First(snap, RunTreeLane.Weapon, RunTreeNodeKind.Fork, false);
                if (fork >= 0)
                {
                    shop.Tree.MarkOwned(fork);
                }

                int maxed = First(snap, RunTreeLane.Weapon, RunTreeNodeKind.Tier, true);
                if (maxed >= 0 && shop.BuildForTree != null)
                {
                    shop.BuildForTree.SetTier(snap.Nodes[maxed].Branch, snap.Nodes[maxed].MaxTier);
                }

                run.Wallet.DebugSet(70);   // the wallet's Changed redraws the screen
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Frame(folder, "tree-states-weapon");

                int closed = FirstWith(tree.Snapshot, RunTreeNodeStatus.Excluded);
                if (closed >= 0)
                {
                    tree.FocusNode(closed);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Frame(folder, "tree-states-closed");
                }

                if (maxed >= 0)
                {
                    tree.FocusNode(maxed);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Frame(folder, "tree-states-max");
                }

                int shortOf = FirstWith(tree.Snapshot, RunTreeNodeStatus.Unaffordable);
                if (shortOf >= 0)
                {
                    tree.FocusNode(shortOf);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Frame(folder, "tree-states-short");
                }

                if (fork >= 0)
                {
                    tree.FocusNode(fork);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Frame(folder, "tree-states-reforge");
                }

                int hearth = tree.Snapshot.HearthIndex;
                if (hearth >= 0)
                {
                    tree.FocusNode(hearth);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Frame(folder, "tree-states-hearth");
                    tree.OnBuy();
                    yield return new WaitForSecondsRealtime(0.35f);
                    yield return Frame(folder, "tree-states-hearth-stoked");
                }
            }

            /// <summary>The first node of a lane and kind (a tier: the first one; otherwise one neither owned nor closed).</summary>
            private static int First(RunTreeSnapshot snap, RunTreeLane lane, RunTreeNodeKind kind, bool anyStatus)
            {
                for (int i = 0; i < snap.Count; i++)
                {
                    RunTreeNodeView n = snap.Nodes[i];
                    if (n.Lane != lane || n.Kind != kind)
                    {
                        continue;
                    }

                    if (anyStatus || (n.Status != RunTreeNodeStatus.Purchased && n.Status != RunTreeNodeStatus.Excluded))
                    {
                        return i;
                    }
                }

                return -1;
            }

            /// <summary>The offer's best buy for the staging: keystone, fork, capstone, then any affordable offered node.</summary>
            private static int BestOffered(RunTreeSnapshot snap)
            {
                int best = -1;
                int bestRank = int.MaxValue;
                for (int i = 0; i < snap.Count; i++)
                {
                    RunTreeNodeView n = snap.Nodes[i];
                    if (!n.InOffer || n.Status != RunTreeNodeStatus.Available)
                    {
                        continue;
                    }

                    int rank = n.Kind == RunTreeNodeKind.BranchKeystone ? 0 : n.Kind == RunTreeNodeKind.Fork ? 1 : n.Kind == RunTreeNodeKind.Capstone ? 2 : 3;
                    if (rank < bestRank)
                    {
                        bestRank = rank;
                        best = i;
                    }
                }

                return best;
            }

            /// <summary>A weapon-lane node still out of reach (prefer the capstone) to pin, so the 4 px path shows.</summary>
            private static int PinTarget(RunTreeSnapshot snap)
            {
                int fallback = -1;
                for (int i = 0; i < snap.Count; i++)
                {
                    RunTreeNodeView n = snap.Nodes[i];
                    if (n.Lane != RunTreeLane.Weapon || n.Kind == RunTreeNodeKind.Hearth ||
                        (n.Status != RunTreeNodeStatus.PrereqMissing && n.Status != RunTreeNodeStatus.Locked))
                    {
                        continue;
                    }

                    if (n.Kind == RunTreeNodeKind.Capstone)
                    {
                        return i;
                    }

                    if (fallback < 0)
                    {
                        fallback = i;
                    }
                }

                return fallback;
            }

            private static int FirstWith(RunTreeSnapshot snap, RunTreeNodeStatus status)
            {
                for (int i = 0; i < snap.Count; i++)
                {
                    if (snap.Nodes[i].Status == status)
                    {
                        return i;
                    }
                }

                return -1;
            }

            private static IEnumerator StagedSpecial(string folder, string name, Func<SpecialWeaponHudState, SpecialWeaponHudState> stage)
            {
                if (WeaponEvents.Special.Phase != SpecialWeaponPhase.Active)
                {
                    yield break;
                }

                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 0.35f)
                {
                    WeaponEvents.PublishSpecial(stage(WeaponEvents.Special), false);
                    yield return null;
                }

                WeaponEvents.PublishSpecial(stage(WeaponEvents.Special), false);
                yield return Frame(folder, name);
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

            /// <summary>
            /// 12b (AD R1-A SHOULD-5, R1-B SHOULD-5): a 4× nearest-neighbour crop of this frame around a HUD element (the first
            /// active one named <paramref name="element"/> under the RunHudScreen), <paramref name="margin"/> canvas px around
            /// its rect, so a 1-2 px stroke can be judged. Without the element the crop is the screen centre.
            /// </summary>
            private static IEnumerator Crop(string folder, string name, string element, float margin)
            {
                yield return new WaitForEndOfFrame();
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                float toFrame = Screen.width > 0 ? frame.width / (float)Screen.width : 1f;
                Rect area = new Rect(Screen.width * 0.5f - 64f, Screen.height * 0.5f - 64f, 128f, 128f);
                RectTransform target = FindHud(element);
                if (target != null)
                {
                    Canvas canvas = target.GetComponentInParent<Canvas>();
                    float scale = canvas != null ? canvas.scaleFactor : 1f;
                    var corners = new Vector3[4];
                    target.GetWorldCorners(corners);
                    Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                    Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                    Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                    float pad = margin * scale;
                    area = Rect.MinMaxRect(Mathf.Min(min.x, max.x) - pad, Mathf.Min(min.y, max.y) - pad, Mathf.Max(min.x, max.x) + pad, Mathf.Max(min.y, max.y) + pad);
                }

                int x0 = Mathf.Clamp(Mathf.FloorToInt(area.xMin * toFrame), 0, frame.width - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(area.yMin * toFrame), 0, frame.height - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(area.xMax * toFrame), x0 + 1, frame.width);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(area.yMax * toFrame), y0 + 1, frame.height);
                int w = x1 - x0, h = y1 - y0;
                Color32[] source = frame.GetPixels32();
                var zoom = new Texture2D(w * 4, h * 4, TextureFormat.RGBA32, false);
                var pixels = new Color32[w * 4 * h * 4];
                for (int y = 0; y < h * 4; y++)
                {
                    for (int x = 0; x < w * 4; x++)
                    {
                        pixels[y * w * 4 + x] = source[(y0 + y / 4) * frame.width + x0 + x / 4];
                    }
                }

                zoom.SetPixels32(pixels);
                zoom.Apply(false, false);
                string path = Path.Combine(folder, name + ".png");
                File.WriteAllBytes(path, zoom.EncodeToPNG());
                Debug.Log(string.Format("[UiCapture] crop {0} · {1} at {2},{3} {4}x{5} px × 4", path, target != null ? element : "centre", x0, y0, w, h));
                Destroy(zoom);
                Destroy(frame);
            }

            private static RectTransform FindHud(string element)
            {
                RunHudScreen hud = FindObjectOfType<RunHudScreen>();
                if (hud == null)
                {
                    return null;
                }

                foreach (RectTransform rect in hud.GetComponentsInChildren<RectTransform>(false))
                {
                    if (rect.name == element)
                    {
                        return rect;
                    }
                }

                return null;
            }

            /// <summary>
            /// 12b · E3 (§12 matrix, the menus' in-process part): <c>-ddUiCapture menumatrix</c> - the main menu and its
            /// Settings pane at UI scale {0.9, 1.0, 1.2} × TextScale {1, 1.25} at this Player's resolution, the menu scene
            /// reloaded for each (the scale applies to the next screen); frames <c>menu-ui1.2-ts1.25.png</c> /
            /// <c>settings-ui1.2-ts1.25.png</c> in <c>-ddUiCaptureOut</c>. The resolutions (1080, 21:9, Mac, 16:10) are the
            /// runner's, one Player launch each; the settings go to this process's own folder, as every capture's do.
            /// </summary>
            private IEnumerator MenuMatrix()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                foreach (float ui in new[] { 0.9f, 1f, 1.2f })
                {
                    foreach (float ts in new[] { 1f, 1.25f })
                    {
                        UiSettings.UiScale = ui;
                        UiSettings.TextScale = ts;
                        MainMenuScreen before = FindObjectOfType<MainMenuScreen>();
                        PrototypeUiFlow.GoToMainMenu();
                        yield return WaitFor(() => { MainMenuScreen now = FindObjectOfType<MainMenuScreen>(); return now != null && now != before; }, 30f);
                        yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                        yield return new WaitForSecondsRealtime(_delay);
                        string tag = "ui" + ui.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + "-ts" + ts.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
                        yield return Frame(folder, "menu-" + tag);

                        MainMenuScreen menu = FindObjectOfType<MainMenuScreen>();
                        if (menu != null)
                        {
                            menu.ShowPane(UiContext.Settings);
                            yield return new WaitForSecondsRealtime(0.8f);
                            yield return Frame(folder, "settings-" + tag);
                        }
                    }
                }

                Debug.Log("[UiCapture] menumatrix done · framebuffer " + Screen.width + "x" + Screen.height);
                Application.Quit();
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
