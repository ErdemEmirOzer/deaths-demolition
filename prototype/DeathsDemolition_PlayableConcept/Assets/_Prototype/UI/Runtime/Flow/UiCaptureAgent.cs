// H12 (C-00 30.09): the capture / perf agent exists only in development builds and the Editor.
#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
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
    /// R4 WP-106: <c>prepare</c> - Prepare Run v2 (default, weapon changed, preview turned, difficulty; UiCaptureAgent.Prepare.cs).
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
    public static partial class UiCaptureAgent
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
            // WP-081: a still frame needs a still world - the menu's cinematic held at S01's start (or -ddMenuWorldTime s);
            // the cinematic modes drive the clock themselves.
            if (!screen.ToLowerInvariant().StartsWith("cinematic", StringComparison.Ordinal) && screen.ToLowerInvariant() != UiMenuDemoAgent.Mode)
            {
                float worldTime;
                MenuCinematic.TimeOverride = float.TryParse(Arg("-ddMenuWorldTime"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out worldTime) ? worldTime : 0f;
            }
            var carrier = new GameObject("UiCaptureAgent");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            if (screen.ToLowerInvariant() == "hud")
            {
                carrier.AddComponent<HudObserver>().Configure(Arg(OutFlag), Arg(RoundsFlag), Arg(DelayFlag), Arg(BotFlag) != null);
                return;
            }

            // WP-098A (HAREKET-MENU §8): the 30 s menu v5 demo - the script and its real-time frames (UiMenuDemoAgent).
            if (screen.ToLowerInvariant() == UiMenuDemoAgent.Mode)
            {
                MenuCinematic.TimeOverride = null;   // the live loop: the freeze and the cuts as a player sees them
                carrier.AddComponent<UiMenuDemoAgent>().Configure(Arg(OutFlag), Arg("-ddUiCaptureFlip"));
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

            // -ddUiLanguage tr: the Turkish pack (process-local settings) - the overflow frames at 1.25 and Mac.
            if (Arg("-ddUiLanguage") == "tr")
            {
                UiSettings.Language = "tr";
            }

            // -ddUiHudFrame 1: the HUD's 16:9 frame on (12c · B12; process-local settings, never the player's file).
            if (Arg("-ddUiHudFrame") == "1")
            {
                UiSettings.HudFrame169 = true;
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

        /// <summary>
        /// WP-068 Turkish (C-00 26.09, "metin taşmaları"): every visible label that TMP truncated, or whose one-line words are
        /// wider than its box, logged with the frame ("[UiCapture] overflow &lt;frame&gt; &lt;path&gt; 'words'") - the
        /// objective half of the 1.25 / Mac overflow check (grep the logs); the frames are the other half.
        /// </summary>
        private static void LogOverflow(string frame)
        {
            foreach (TMPro.TMP_Text text in UnityEngine.Object.FindObjectsOfType<TMPro.TMP_Text>())
            {
                if (!text.isActiveAndEnabled || text.canvasRenderer.GetInheritedAlpha() < 0.05f || string.IsNullOrEmpty(text.text))
                {
                    continue;
                }

                float width = text.rectTransform.rect.width;
                bool wide = !text.enableWordWrapping && width > 1f && text.preferredWidth > width + 2f;
                if (text.isTextTruncated || wide)
                {
                    string where = text.name;
                    for (Transform t = text.transform.parent; t != null && t.parent != null; t = t.parent)
                    {
                        where = t.name + "/" + where;
                    }

                    Debug.Log("[UiCapture] overflow " + frame + " " + where + " '" + text.text.Replace("\n", " ") + "'"
                              + (text.isTextTruncated ? " truncated" : " wider " + text.preferredWidth.ToString("F0") + " > " + width.ToString("F0") + " " + text.overflowMode));
                }
            }
        }

        private static void WritePng(string path)
        {
            LogOverflow(Path.GetFileNameWithoutExtension(path));
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

        /// <summary>
        /// WP-068 12b · direction §8 (C-00 30.09 R1-E d): <c>-ddUiPerf &lt;blockSeconds&gt;</c> measures the staged screen's UI
        /// before its frame - <c>uiperf-&lt;surface&gt;.json</c> in the capture folder and one <c>[UiPerf]</c> log line.
        /// Method as PilotPerfProbe 04h: every enabled root canvas on, then off, in alternating blocks (on/off × 5, settle
        /// 0.75 s), the frame cap and vsync lifted for the window (a cap resets LastValue - the WP-059 lesson); markers are
        /// found by name through <see cref="ProfilerRecorderHandle.GetAvailable"/> and listed +/-. UI main-thread cost =
        /// Δ(Update + LateUpdate scripts) + Δ(PlayerUpdateCanvases + PlayerEmitCanvasGeometry + RenderOverlays); p99 over the
        /// on-frames' UI markers; GPU = Δ FrameTimingManager gpuFrameTime; TMP regen = TMP.GenerateText samples per frame.
        /// A main-menu pane also records one re-open (ShowPane) in ms and its frame's GC bytes.
        /// </summary>
        private static class UiPerf
        {
            private const string Flag = "-ddUiPerf";
            private const int Blocks = 10;
            private const float SettleSeconds = 0.75f;
            private const int MaxFrames = 8192;

            // 0-1 scripts, 2-4 the UI frame cost, 5-8 detail (inside the others; never added).
            private static readonly string[] MarkerNames =
            {
                "Update.ScriptRunBehaviourUpdate",
                "PreLateUpdate.ScriptRunBehaviourLateUpdate",
                "PostLateUpdate.PlayerUpdateCanvases",
                "PostLateUpdate.PlayerEmitCanvasGeometry",
                "UGUI.Rendering.RenderOverlays",
                "Canvas.SendWillRenderCanvases",
                "Canvas.BuildBatch",
                "EventSystem.Update",
                "TMP.GenerateText",
            };

            private const int TmpMarker = 8;
            private static readonly FrameTiming[] s_timings = new FrameTiming[1];

            public static bool Requested
            {
                get { return Arg(Flag) != null; }
            }

            public static IEnumerator Measure(string surface, string folder)
            {
                float blockSeconds;
                if (!float.TryParse(Arg(Flag), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out blockSeconds) || blockSeconds <= 0f)
                {
                    blockSeconds = 3f;
                }

                var canvases = new System.Collections.Generic.List<Canvas>();
                foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
                {
                    if (canvas.isRootCanvas && canvas.enabled)
                    {
                        canvases.Add(canvas);
                    }
                }

                var available = new System.Collections.Generic.List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(available);
                var markers = new ProfilerRecorder[MarkerNames.Length];
                var found = new System.Text.StringBuilder();
                for (int m = 0; m < MarkerNames.Length; m++)
                {
                    for (int h = 0; h < available.Count; h++)
                    {
                        if (ProfilerRecorderHandle.GetDescription(available[h]).Name == MarkerNames[m])
                        {
                            markers[m] = new ProfilerRecorder(available[h], 1, ProfilerRecorderOptions.Default);
                            markers[m].Start();
                            break;
                        }
                    }

                    found.Append(markers[m].Valid ? "+" : "-").Append(MarkerNames[m]).Append(' ');
                }

                ProfilerRecorder setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                ProfilerRecorder batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                ProfilerRecorder draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                ProfilerRecorder gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                int capBefore = Application.targetFrameRate;
                int vsyncBefore = QualitySettings.vSyncCount;
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 0;

                // [0] = UI on, [1] = UI off
                var frames = new int[2];
                var frameMs = new double[2];
                var markerSum = new double[2, MarkerNames.Length];
                var tmpCount = new double[2];
                var counters = new double[2, 4];
                var gpuSum = new double[2];
                var gpuN = new int[2];
                var uiOn = new float[MaxFrames];
                int uiOnN = 0;
                for (int block = 0; block < Blocks; block++)
                {
                    int k = block % 2;
                    for (int c = 0; c < canvases.Count; c++)
                    {
                        if (canvases[c] != null)
                        {
                            canvases[c].enabled = k == 0;
                        }
                    }

                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < SettleSeconds + blockSeconds)
                    {
                        yield return null;
                        if (Time.realtimeSinceStartup - t0 < SettleSeconds)
                        {
                            continue;
                        }

                        frames[k]++;
                        frameMs[k] += Time.unscaledDeltaTime * 1000.0;
                        double ui = 0d;
                        for (int m = 0; m < markers.Length; m++)
                        {
                            if (!markers[m].Valid)
                            {
                                continue;
                            }

                            double ms = markers[m].LastValue / 1e6;
                            markerSum[k, m] += ms;
                            if (m >= 2 && m <= 4)
                            {
                                ui += ms;
                            }
                        }

                        if (markers[TmpMarker].Valid && markers[TmpMarker].Count > 0)
                        {
                            tmpCount[k] += markers[TmpMarker].GetSample(0).Count;
                        }

                        if (k == 0 && uiOnN < MaxFrames)
                        {
                            uiOn[uiOnN++] = (float)ui;
                        }

                        counters[k, 0] += setPass.Valid ? setPass.LastValue : 0;
                        counters[k, 1] += batches.Valid ? batches.LastValue : 0;
                        counters[k, 2] += draws.Valid ? draws.LastValue : 0;
                        counters[k, 3] += gc.Valid ? gc.LastValue : 0;
                        FrameTimingManager.CaptureFrameTimings();
                        if (FrameTimingManager.GetLatestTimings(1, s_timings) > 0 && s_timings[0].gpuFrameTime > 0d)
                        {
                            gpuSum[k] += s_timings[0].gpuFrameTime;
                            gpuN[k]++;
                        }
                    }
                }

                for (int c = 0; c < canvases.Count; c++)
                {
                    if (canvases[c] != null)
                    {
                        canvases[c].enabled = true;
                    }
                }

                // One re-open of a main-menu pane (direction §8: a screen opens once in ≤ 12 ms, ≤ 256 KB).
                double openMs = -1d;
                long openGc = -1L;
                MainMenuScreen menu = UnityEngine.Object.FindObjectOfType<MainMenuScreen>();
                UiContext pane = UiNavigation.Layer;
                if (menu != null && UiNavigationModel.IsLayer(pane))
                {
                    menu.ShowPane(pane == UiContext.Profile ? UiContext.Settings : UiContext.Profile);
                    yield return new WaitForSecondsRealtime(1f);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    menu.ShowPane(pane);
                    watch.Stop();
                    openMs = watch.Elapsed.TotalMilliseconds;
                    yield return null;
                    openGc = gc.Valid ? gc.LastValue : -1L;
                    yield return new WaitForSecondsRealtime(0.5f);
                }

                Application.targetFrameRate = capBefore;
                QualitySettings.vSyncCount = vsyncBefore;
                var valid = new bool[markers.Length];
                for (int m = 0; m < markers.Length; m++)
                {
                    valid[m] = markers[m].Valid;
                    if (valid[m])
                    {
                        markers[m].Dispose();
                    }
                }

                setPass.Dispose();
                batches.Dispose();
                draws.Dispose();
                gc.Dispose();

                double Avg(double sum, int k2) => frames[k2] > 0 ? sum / frames[k2] : -1d;
                double Marker(int k2, int m) => valid[m] ? Avg(markerSum[k2, m], k2) : -1d;
                double scriptsDelta = Avg(markerSum[0, 0] + markerSum[0, 1], 0) - Avg(markerSum[1, 0] + markerSum[1, 1], 1);
                double uiMarkersOn = Avg(markerSum[0, 2] + markerSum[0, 3] + markerSum[0, 4], 0);
                double uiMarkersOff = Avg(markerSum[1, 2] + markerSum[1, 3] + markerSum[1, 4], 1);
                Array.Sort(uiOn, 0, uiOnN);
                double p99 = uiOnN > 0 ? uiOn[Math.Min(uiOnN - 1, (int)(uiOnN * 0.99))] : -1d;
                double gpuDelta = gpuN[0] > 0 && gpuN[1] > 0 ? gpuSum[0] / gpuN[0] - gpuSum[1] / gpuN[1] : -1d;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                string json = string.Format(ci,
                    "{{\"surface\":\"{0}\",\"screen\":\"{1}x{2}\",\"canvases\":{3},\"framesOn\":{4},\"framesOff\":{5},\"frameMsOn\":{6:F3},\"frameMsOff\":{7:F3}," +
                    "\"uiCostMs\":{8:F3},\"scriptsDeltaMs\":{9:F3},\"uiMarkersOnMs\":{10:F3},\"uiMarkersOffMs\":{11:F3},\"uiMarkersP99Ms\":{12:F3}," +
                    "\"willRenderMs\":{13:F3},\"buildBatchMs\":{14:F3},\"eventSystemMs\":{15:F3},\"tmpRegenPerFrame\":{16:F2}," +
                    "\"gpuDeltaMs\":{17:F3},\"setPassOn\":{18:F1},\"setPassDelta\":{19:F1},\"batchesOn\":{20:F1},\"batchesDelta\":{21:F1},\"drawsDelta\":{22:F1}," +
                    "\"gcBytesPerFrameOn\":{23:F0},\"openMs\":{24:F2},\"openGcBytes\":{25},\"markers\":\"{26}\",\"blockSeconds\":{27:F1} }}",
                    surface, Screen.width, Screen.height, canvases.Count, frames[0], frames[1], Avg(frameMs[0], 0), Avg(frameMs[1], 1),
                    scriptsDelta + uiMarkersOn - uiMarkersOff, scriptsDelta, uiMarkersOn, uiMarkersOff, p99,
                    Marker(0, 5), Marker(0, 6), Marker(0, 7), Avg(tmpCount[0], 0),
                    gpuDelta, Avg(counters[0, 0], 0), Avg(counters[0, 0], 0) - Avg(counters[1, 0], 1), Avg(counters[0, 1], 0),
                    Avg(counters[0, 1], 0) - Avg(counters[1, 1], 1), Avg(counters[0, 2], 0) - Avg(counters[1, 2], 1),
                    Avg(counters[0, 3], 0), openMs, openGc, found.ToString().Trim(), blockSeconds);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "uiperf-" + surface + ".json"), json + "\n");
                }

                Debug.Log("[UiPerf] " + json);
            }
        }

        private sealed partial class Driver : MonoBehaviour
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

                if (_screen == "menuintro")
                {
                    yield return MenuIntroFrames();
                    yield break;
                }

                if (_screen == PrepareMode)
                {
                    // R4 WP-106 (S-10 WP-111): its first line names the screen version (screen=prepareRunV2), then its own waits.
                    yield return PrepareFrames();
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

                if (_screen == "typespec")
                {
                    yield return TypeSpec();
                    yield break;
                }

                if (_screen == "cursor")
                {
                    yield return CursorShot();
                    yield break;
                }

                if (_screen == "loading")
                {
                    yield return LoadingFrames();
                    yield break;
                }

                if (_screen == "watchdog")
                {
                    yield return WatchdogFrames();
                    yield break;
                }

                if (_screen == "missingscene")
                {
                    yield return MissingSceneFrames();
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

                if (_screen == "hudv4" || _screen == "hudv4demo" || _screen == "hudv4plate")
                {
                    yield return _screen == "hudv4" ? HudV4Frames() : _screen == "hudv4plate" ? HudV4Plates() : HudV4Demo();
                    Application.Quit();
                    yield break;
                }

                if (_screen == "menumatrix")
                {
                    yield return MenuMatrix();
                    yield break;
                }

                if (_screen == "cinematic" || _screen == "cinematicperf" || _screen == "cinematicmp4")
                {
                    yield return _screen == "cinematic" ? CinematicFrames() : _screen == "cinematicperf" ? CinematicPerf() : CinematicMp4();
                    Debug.Log("[UiCapture] " + _screen + " done");
                    Application.Quit();
                    yield break;
                }

                if (_screen == "windows")
                {
                    yield return WindowsFrames();
                    Debug.Log("[UiCapture] windows done");
                    Application.Quit();
                    yield break;
                }

                if (_screen == "menuv4")
                {
                    yield return MenuV4Frames();
                    Debug.Log("[UiCapture] menuv4 done");
                    Application.Quit();
                    yield break;
                }

                if (_screen == "tree")
                {
                    yield return TreeFrames();
                    Debug.Log("[UiCapture] tree done");
                    Application.Quit();
                    yield break;
                }

                if (_screen == "latebonus")
                {
                    yield return LateBonusFrames();
                    Debug.Log("[UiCapture] latebonus done");
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
                            if (UiPerf.Requested)
                            {
                                // The live HUD at round start (the 40-body HUD is PilotPerfProbe 04h's).
                                yield return UiPerf.Measure("hud-live", Path.GetDirectoryName(_out));
                            }

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

                string folder = Path.GetDirectoryName(_out);
                if (UiPerf.Requested)
                {
                    yield return UiPerf.Measure(_screen, folder);
                }

                yield return new WaitForEndOfFrame();

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
            /// <summary>
            /// 12b SHOULD-7 (AD G1 SHOULD-4): the hardware cursor is not in a Player capture, so this mode only stages it -
            /// the kit sheet, the Press art bound (UiCursor.SetPressed, as a held control does), and the cream SELECTED
            /// chip's centre written to <c>&lt;out&gt;.target</c> ("x y width height", frame px, origin bottom-left). The
            /// capture script moves the macOS pointer there (tools/warp-cursor.swift) and takes the screen with the cursor
            /// (<c>screencapture -C</c>) inside the 20 s this mode holds; then a reference frame without it, and quit.
            /// </summary>
            private IEnumerator CursorShot()
            {
                UiKitSheet.Build(1);
                yield return new WaitForSecondsRealtime(_delay);
                RectTransform chip = null;
                foreach (RectTransform rect in UnityEngine.Object.FindObjectsOfType<RectTransform>())
                {
                    if (rect.name == "Selected" && rect.gameObject.activeInHierarchy)
                    {
                        chip = rect;
                        break;
                    }
                }

                Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                if (chip != null)
                {
                    var corners = new Vector3[4];
                    chip.GetWorldCorners(corners);
                    Canvas canvas = chip.GetComponentInParent<Canvas>();
                    Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                    centre = (RectTransformUtility.WorldToScreenPoint(camera, corners[0]) + RectTransformUtility.WorldToScreenPoint(camera, corners[2])) * 0.5f;
                }

                UiCursor.SetContextVisible(true);
                UiCursor.SetPressed(true);
                File.WriteAllText(_out + ".target", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:F1} {1:F1} {2} {3}", centre.x, centre.y, Screen.width, Screen.height));
                Debug.Log("[UiCapture] cursor target " + centre + " on " + (chip != null ? "the SELECTED chip" : "the screen centre")
                          + " · framebuffer " + Screen.width + "x" + Screen.height + " · Press art " + UiCursor.Pressed);
                yield return new WaitForSecondsRealtime(20f);
                yield return new WaitForEndOfFrame();
                WritePng(_out);
                UiCursor.SetPressed(false);
                Application.Quit();
            }

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
            /// DEC-109: <c>-ddUiCapture typespec</c> - the Display face (Shippori Mincho B1 DD-TR) as TMP sets it in this Player,
            /// through the path every heading takes (UiFactory.Label, the Display role, its static atlas and kerning): Turkish
            /// and English at 56 / 40 / 28 px times <c>-ddUiKitScale</c> (default 1.25, the AD's scale) - real rows upper-cased
            /// with the language's casing - ink on the page surface, to <c>-ddUiCaptureOut</c>. Overflow is logged.
            /// </summary>
            private IEnumerator TypeSpec()
            {
                float scale = 1.25f;
                string scaleArg = Arg("-ddUiKitScale");
                if (!string.IsNullOrEmpty(scaleArg))
                {
                    float.TryParse(scaleArg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out scale);
                }

                UiTokens.TextScale = Mathf.Clamp(scale, UiSettings.TextScaleMin, UiSettings.TextScaleMax);
                Canvas canvas = UiFactory.CreateCanvas("TypeSpec", UiLayer.Modal, null, UiFactory.EnsureUiCamera());
                var root = (RectTransform)canvas.transform;
                UnityEngine.UI.Image page = UiFactory.NewImage("Page", root, UiColor.SurfaceBase, UiSprites.Solid);
                UiFactory.Fill((RectTransform)page.transform);
                string language = UiStrings.Language;
                float y = 40f;
                // Real copy, never literals (UiTextAuditTests): these rows' Turkish carries Ğ İ Ş Ç Ö Ü in upper case.
                string[][] rows =
                {
                    new[] { "settings.row.ui_scale", "weapon.class.sr" },
                    new[] { "camera.fov.third_person", "tree.meta.passive.qol.tree_preview.name" },
                };
                foreach (string set in new[] { UiStrings.Turkish, UiStrings.English })
                {
                    UiStrings.Language = set;   // the face UiFonts.For picks for this language, and its casing
                    var lines = new string[rows.Length];
                    for (int r = 0; r < rows.Length; r++)
                    {
                        lines[r] = UiStrings.Upper(UiStrings.Get(rows[r][0])) + "  ·  " + UiStrings.Upper(UiStrings.Get(rows[r][1]));
                    }

                    foreach (int px in new[] { 56, 40, 28 })
                    {
                        for (int i = 0; i < lines.Length; i++)
                        {
                            float size = px * UiTokens.TextScale;
                            TMPro.TMP_Text line = UiFactory.Label("Spec" + set + px, root, lines[i], UiType.Display, UiColor.TextPrimary, TextAnchor.UpperLeft);
                            line.fontSize = size;
                            var rect = (RectTransform)line.transform;
                            rect.anchorMin = new Vector2(0f, 1f);
                            rect.anchorMax = new Vector2(0f, 1f);
                            rect.pivot = new Vector2(0f, 1f);
                            rect.sizeDelta = new Vector2(1800f, size * 1.25f);
                            rect.anchoredPosition = new Vector2(56f, -y);
                            y += size * 1.25f;
                        }
                    }

                    y += 24f;
                }

                UiStrings.Language = language;
                yield return new WaitForSecondsRealtime(_delay);
                yield return new WaitForEndOfFrame();
                WritePng(_out);
                yield return null;
                Debug.Log("[UiCapture] typespec done · text scale " + UiTokens.TextScale + " · framebuffer " + Screen.width + "x" + Screen.height);
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
                // AP-035 R3 (S3-32): the logo's intro over the key art - wordmark inking, the seal falling, the impact, the
                // canonical frame (x 1.33: impact 1.44 s, canonical 1.69 s), later the loop; the pose each frame shows is logged.
                foreach (float at in LogoIntroShots)
                {
                    yield return WaitFor(() => Time.realtimeSinceStartup - groupAt >= at, 10f);
                    yield return LogoFrame(folder, "loading-logo-t" + Mathf.RoundToInt(at * 100f).ToString("000"), curtain.Logo);
                }

                yield return WaitFor(() => curtain.WetFill >= 0.45f, 10f);
                yield return Frame(folder, "loading-run-05");
                yield return WaitFor(() => Time.realtimeSinceStartup - groupAt >= 3f, 10f);
                yield return LogoFrame(folder, "loading-logo-loop", curtain.Logo);
                yield return WaitFor(() => Time.realtimeSinceStartup - groupAt >= 4f, 10f);
                yield return Frame(folder, "loading-run-t4");
                Debug.Log("[UiCapture] loading-run-t4 keyArtScale=" + curtain.KeyArtScale.ToString("F4") + " t=" + (Time.realtimeSinceStartup - groupAt).ToString("F2")
                          + " edge=" + ((curtain.KeyArtScale - 1f) * Screen.width * 0.5f).ToString("F1") + "px");
                yield return WaitFor(() => curtain.EyebrowText == "ENTERING", 15f);
                yield return new WaitForSecondsRealtime(0.1f);
                yield return Frame(folder, "loading-run-entering");
                PrototypeUiFlow.ArtificialLoadSeconds = 0f;
                // AP-035 R3: the outro over the parting doors (hold, the wordmark wiped, the seal under ink).
                yield return WaitFor(() => curtain.LogoPhase == UiLogoPhase.Outro || curtain.LogoPhase == UiLogoPhase.FadeOut, 30f);
                float outroAt = Time.realtimeSinceStartup;
                yield return WaitFor(() => Time.realtimeSinceStartup - outroAt >= 0.40f, 2f);
                yield return LogoFrame(folder, "loading-logo-outro", curtain.Logo);
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
            /// R1-E E4 V4–V6 (plan K/wp-068e/E4-WATCHDOG-HATA-MODALI-PLANI.md): <c>-ddUiCapture watchdog</c> - a real stalled
            /// load in the Player. Selection → Run with the activation held: the stall notice (≥ StallSeconds) and the QUIT-only
            /// error (≥ StallErrorSeconds) are timed and framed; a simulated Cancel 1 s after the error must leave the error, the
            /// scene and the load alone; at <c>-ddUiLoadHold</c> s (default 75) the hold is released and the run must start with
            /// the error gone. <c>-ddUiLoadQuit 1</c> submits QUIT on the error instead (the harness times the exit and compares
            /// the profile). Ends with <c>[UiWatchdog] PASS|FAIL</c> and exit 0 / 3.
            /// </summary>
            private IEnumerator WatchdogFrames()
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                float hold;
                if (!float.TryParse(Arg("-ddUiLoadHold"), System.Globalization.NumberStyles.Float, ci, out hold) || hold <= 0f)
                {
                    hold = 75f;
                }

                PrototypeUiFlow.GoToSelection();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Selection, 30f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                yield return new WaitForSecondsRealtime(0.5f);
                UiLoadTiming.RecordLoad(PrototypeScenes.Run, 5f);
                PrototypeUiFlow.ArtificialLoadSeconds = 1000000f;
                UiCurtain curtain = UiCurtain.Instance;
                float requestAt = Time.realtimeSinceStartup;
                PrototypeUiFlow.StartRun();
                float noticeAt = -1f;
                float errorAt = -1f;
                while (errorAt < 0f && Time.realtimeSinceStartup - requestAt < 90f)
                {
                    if (noticeAt < 0f && curtain.StallVisible)
                    {
                        noticeAt = Time.realtimeSinceStartup;
                        yield return Frame(folder, "watchdog-stalled");
                    }

                    if (curtain.ErrorOpen)
                    {
                        errorAt = Time.realtimeSinceStartup;
                    }

                    yield return null;
                }

                string scene = SceneManager.GetActiveScene().name;
                Debug.Log(string.Format(ci, "[UiWatchdog] notice {0:F1} s · error {1:F1} s · scene {2} · loading {3}",
                    noticeAt < 0f ? -1f : noticeAt - requestAt, errorAt < 0f ? -1f : errorAt - requestAt, scene, PrototypeUiFlow.IsLoading));
                if (errorAt < 0f)
                {
                    PrototypeUiFlow.ArtificialLoadSeconds = 0f;
                    Debug.Log("[UiWatchdog] FAIL: no error by 90 s");
                    Application.Quit(3);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.6f);
                yield return Frame(folder, "watchdog-error");
                UiModal modal = curtain.GetComponentInChildren<UiModal>();
                bool quitOnly = modal != null && modal.Primary != null && modal.Secondary == null;
                yield return new WaitForSecondsRealtime(0.4f);
                UiInput.SimulateCancelNextFrame();
                yield return null;
                yield return null;
                bool cancelIgnored = curtain.ErrorOpen && PrototypeUiFlow.IsLoading && SceneManager.GetActiveScene().name == scene;
                Debug.Log("[UiWatchdog] error QUIT-only " + quitOnly + " · cancel under the error " + (cancelIgnored ? "ignored" : "ACTED"));
                if (Arg("-ddUiLoadQuit") == "1" && modal != null && modal.Primary != null)
                {
                    Debug.Log(string.Format(ci, "[UiWatchdog] QUIT submitted at {0:F2}", Time.realtimeSinceStartup));
                    UnityEngine.EventSystems.ExecuteEvents.Execute(modal.Primary.gameObject,
                        new UnityEngine.EventSystems.BaseEventData(UnityEngine.EventSystems.EventSystem.current), UnityEngine.EventSystems.ExecuteEvents.submitHandler);
                    yield break;
                }

                while (Time.realtimeSinceStartup - requestAt < hold)
                {
                    yield return null;
                }

                PrototypeUiFlow.ArtificialLoadSeconds = 0f;
                float releaseAt = Time.realtimeSinceStartup;
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Run, 30f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 15f);
                bool recovered = SceneManager.GetActiveScene().name == PrototypeScenes.Run && !curtain.ErrorOpen;
                Debug.Log(string.Format(ci, "[UiWatchdog] released at {0:F1} s · recovered {1} in {2:F1} s",
                    releaseAt - requestAt, recovered ? "yes" : "NO", Time.realtimeSinceStartup - releaseAt));
                yield return new WaitForSecondsRealtime(1f);
                yield return Frame(folder, "watchdog-recovered");
                bool pass = noticeAt - requestAt >= PrototypeUiFlow.StallSeconds - 0.1f
                            && errorAt - requestAt >= PrototypeUiFlow.StallErrorSeconds - 0.1f
                            && quitOnly && cancelIgnored && recovered;
                Debug.Log("[UiWatchdog] " + (pass ? "PASS" : "FAIL"));
                Application.Quit(pass ? 0 : 3);
            }

            /// <summary>
            /// R1-E E4 V2/V6 in the Player: <c>-ddUiCapture missingscene</c> - from the main menu's Settings pane a load of a
            /// scene that is not in the build must open the load error over the menu (primary focused, nothing loading); a
            /// simulated Cancel must leave the pane, the focus and the error; the primary must bring the main menu back; and,
            /// as the control, the same Cancel without the error must take the menu's back (Settings → Profile). Frames
            /// <c>missing-error</c> / <c>missing-after-cancel</c>; ends with <c>[UiMissingScene] PASS|FAIL</c> and exit 0 / 3.
            /// </summary>
            private IEnumerator MissingSceneFrames()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                MainMenuScreen menu = FindObjectOfType<MainMenuScreen>();
                if (menu == null)
                {
                    Debug.Log("[UiMissingScene] FAIL: no main menu");
                    Application.Quit(3);
                    yield break;
                }

                menu.ShowPane(UiContext.Settings);
                yield return new WaitForSecondsRealtime(0.5f);
                UiCurtain curtain = UiCurtain.Instance;
                PrototypeUiFlow.LoadForDiagnostics("DD_R1E_NoSuchScene");
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Frame(folder, "missing-error");
                UiModal modal = curtain.GetComponentInChildren<UiModal>();
                UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
                bool opened = curtain.ErrorOpen && modal != null && modal.Primary != null && modal.Secondary != null && !PrototypeUiFlow.IsLoading;
                bool focused = opened && events != null && events.currentSelectedGameObject == modal.Primary.gameObject;
                UiInput.SimulateCancelNextFrame();
                yield return null;
                yield return null;
                bool ignored = curtain.ErrorOpen && UiNavigation.Layer == UiContext.Settings
                               && events != null && modal != null && events.currentSelectedGameObject == modal.Primary.gameObject;
                yield return Frame(folder, "missing-after-cancel");
                bool returned = false;
                if (opened)
                {
                    UnityEngine.EventSystems.ExecuteEvents.Execute(modal.Primary.gameObject,
                        new UnityEngine.EventSystems.BaseEventData(events), UnityEngine.EventSystems.ExecuteEvents.submitHandler);
                    yield return new WaitForSecondsRealtime(0.2f);
                    yield return WaitFor(() => !PrototypeUiFlow.IsLoading && !UiCurtain.IsCovering, 20f);
                    returned = !curtain.ErrorOpen && SceneManager.GetActiveScene().name == PrototypeScenes.MainMenu;
                }

                bool control = false;
                menu = FindObjectOfType<MainMenuScreen>();
                if (menu != null)
                {
                    menu.ShowPane(UiContext.Settings);
                    yield return new WaitForSecondsRealtime(0.3f);
                    UiInput.SimulateCancelNextFrame();
                    yield return null;
                    yield return null;
                    control = UiNavigation.Layer == UiContext.Profile;
                }

                bool pass = opened && focused && ignored && returned && control;
                Debug.Log("[UiMissingScene] opened " + opened + " · focus on primary " + focused + " · cancel ignored " + ignored +
                          " · primary returned " + returned + " · control cancel reached the menu " + control + " · " + (pass ? "PASS" : "FAIL"));
                Application.Quit(pass ? 0 : 3);
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
            /// WP-094 HUD v4 (<c>-ddUiCapture hudv4</c>, 1920 × 1080 top-down; the acceptance frames of K/c-00/R3/prompts/WP-094-098A.md
            /// "Kabul"): the juice staged on a live run with REAL events only - wallet credits, PlayerHealth damage and heals,
            /// ConsumeShot / TryBeginReload, FeelEvents.RaiseDryFire (the HUD hears the empty trigger from FeelEvents),
            /// WeaponController.EquipClassic, the round transition
            /// through RunHudScreen.CaptureRound - so every motion in a frame is the production path's (HAREKET-HUD §7 "Çekim").
            /// Frames (<c>v4-*.png</c> in the capture folder): idle; gold-punch (mid-punch) / gold-rest; health-full /
            /// health-damaged (the chunk falling, the trail held) / health-two-hits (two wedges, two edges) / health-low (on a lub)
            /// / health-heal (the moss glow); round-I / IV / IX / XIV / XXIII / XXXVIII / LXXXVIII (each landed; the log line carries
            /// cap, plate, seal and core) and round-reveal-XIV (mid-brush);
            /// mag-full / mag-shot (spark, casing, lean) / mag-low (the LOW pulse) / mag-empty (after an empty trigger) /
            /// mag-reloading (the preview) / mag-reloaded (the slide) / mag-switch (mid-swap) / mag-switched. Add
            /// <c>-ddUiReducedMotion 1</c> for the reduced-motion set, <c>-ddUiReduceFlashes 1</c> for the flash-reduced one.
            /// Each frame logs one <c>[UiCapture] hudv4</c> line with the HUD's juice counters.
            /// </summary>
            private IEnumerator HudV4Frames()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                yield return HudV4Run();
                RunHudScreen hud = FindObjectOfType<RunHudScreen>();
                PlayerRig rig = PlayerRegistry.Primary;
                if (hud == null || rig == null || rig.Health == null)
                {
                    Debug.LogWarning("[UiCapture] hudv4: no HUD / player");
                    yield break;
                }

                PlayerHealth health = rig.Health;
                Vector3 at = rig.transform.position;
                yield return new WaitForSecondsRealtime(1.5f);
                yield return HudV4Frame(folder, "v4-idle", hud);

                // Gold (G1 – G5): one credit, the punch at 50 ms, then the rest.
                if (GoldService.Instance != null)
                {
                    GoldService.Instance.Wallet.Credit(25, at);
                    yield return new WaitForSecondsRealtime(0.05f);
                    yield return HudV4Frame(folder, "v4-gold-punch", hud);
                    yield return new WaitForSecondsRealtime(0.8f);
                    yield return HudV4Frame(folder, "v4-gold-rest", hud);
                }

                // Health (H1 – H5, D1 / D2).
                health.HealToFull();
                yield return new WaitForSecondsRealtime(1.2f);
                yield return HudV4Frame(folder, "v4-health-full", hud);
                yield return HudV4Hit(health, at, Vector3.right, 0.3f);
                yield return new WaitForSecondsRealtime(0.08f);
                yield return HudV4Frame(folder, "v4-health-damaged", hud);
                yield return new WaitForSecondsRealtime(1.2f);
                health.HealToFull();
                yield return new WaitForSecondsRealtime(1.2f);
                health.DiagnosisInvulnerable = false;
                health.ApplyDamage(health.Max * 0.15f, at + Vector3.left * 2f, "ui-capture");
                health.ApplyDamage(health.Max * 0.15f, at + Vector3.forward * 2f, "ui-capture");
                health.DiagnosisInvulnerable = true;
                yield return new WaitForSecondsRealtime(0.05f);
                yield return HudV4Frame(folder, "v4-health-two-hits", hud);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return HudV4Hit(health, at, Vector3.back, 0.5f);
                int beats = hud.HeartBeats;
                yield return WaitFor(() => hud.HeartBeats > beats, 3f);
                yield return new WaitForSecondsRealtime(0.04f);
                yield return HudV4Frame(folder, "v4-health-low", hud);
                health.Heal(health.Max * 0.25f);
                yield return new WaitForSecondsRealtime(0.15f);
                yield return HudV4Frame(folder, "v4-health-heal", hud);
                health.HealToFull();
                yield return new WaitForSecondsRealtime(1.2f);

                // Rounds (R1 – R3): each numeral landed after its transition, and one caught mid-brush.
                foreach (int round in HudV4Rounds)
                {
                    hud.CaptureRound(round);
                    yield return WaitFor(() => !hud.RoundTransitionLive, 4f);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return HudV4Frame(folder, "v4-round-" + RomanNumerals.Cached(round), hud);
                }

                hud.CaptureRound(14);
                yield return new WaitForSecondsRealtime(HudRoundTransition.HandoffSeconds + 0.14f);
                yield return HudV4Frame(folder, "v4-round-reveal-XIV", hud);
                yield return WaitFor(() => !hud.RoundTransitionLive, 4f);

                // Magazine (A1 – A8).
                WeaponController weapon = WeaponRegistry.ActiveController;
                ClassicWeaponRuntime classic = weapon != null ? weapon.Classic : null;
                if (classic != null)
                {
                    if (classic.Ammo < classic.MagazineSize && classic.TryBeginReload())
                    {
                        yield return WaitFor(() => !classic.IsReloading, 6f);
                    }

                    yield return new WaitForSecondsRealtime(0.5f);
                    yield return HudV4Frame(folder, "v4-mag-full", hud);
                    classic.ConsumeShot();
                    yield return null;
                    yield return null;
                    yield return new WaitForSecondsRealtime(0.03f);
                    yield return HudV4Frame(folder, "v4-mag-shot", hud);
                    int low = Mathf.Max(1, Mathf.FloorToInt(classic.MagazineSize * RunHudViewModel.LowAmmoFraction));
                    while (classic.Ammo > low)
                    {
                        classic.ConsumeShot();
                    }

                    yield return new WaitForSecondsRealtime(0.5f);
                    yield return HudV4Frame(folder, "v4-mag-low", hud);
                    while (classic.Ammo > 0)
                    {
                        classic.ConsumeShot();
                    }

                    yield return null;
                    yield return null;
                    yield return new WaitForSecondsRealtime(0.3f);
                    HudV4DryFire();
                    yield return new WaitForSecondsRealtime(0.05f);
                    yield return HudV4Frame(folder, "v4-mag-empty", hud);
                    classic.TryBeginReload();
                    yield return WaitFor(() => classic.ReloadProgress01 >= 0.5f, 6f);
                    yield return HudV4Frame(folder, "v4-mag-reloading", hud);
                    yield return WaitFor(() => !classic.IsReloading, 6f);
                    yield return new WaitForSecondsRealtime(0.08f);
                    yield return HudV4Frame(folder, "v4-mag-reloaded", hud);

                    ClassicWeaponId home = classic.Id;
                    ClassicWeaponId other = home == ClassicWeaponId.CombatShotgun ? ClassicWeaponId.AssaultRifle : ClassicWeaponId.CombatShotgun;
                    weapon.EquipClassic(other);
                    yield return new WaitForSecondsRealtime(0.12f);
                    yield return HudV4Frame(folder, "v4-mag-switch", hud);
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return HudV4Frame(folder, "v4-mag-switched", hud);
                    weapon.EquipClassic(home);
                }

                Debug.Log("[UiCapture] hudv4 done · framebuffer " + Screen.width + "x" + Screen.height + " rm=" + UiMotion.ReducedMotion);
            }

            /// <summary>A real empty pull as the weapon raises it (FeelEvents.DryFire - the one source the HUD hears since 08.10).</summary>
            private static void HudV4DryFire()
            {
                WeaponController weapon = WeaponRegistry.ActiveController;
                PlayerRig rig = PlayerRegistry.Primary;
                DD.Prototype.Feedback.FeelEvents.RaiseDryFire(new DD.Prototype.Feedback.FeelDryFire(
                    weapon != null ? weapon.Classic.Id : ClassicWeaponId.AssaultRifle, rig != null ? rig.transform.position : Vector3.zero));
            }

            /// <summary>The rounds the acceptance asks for (I / IV / IX / XIV / XXIII).</summary>
            // 08.10 (AD SHOULD-3): XXXVIII and LXXXVIII too - the plate fitted to 288 at a shrunk cap (≥ 32), never seen at runtime.
            private static readonly int[] HudV4Rounds = { 1, 4, 9, 14, 23, 38, 88 };

            /// <summary>
            /// WP-094 (<c>-ddUiCapture hudv4demo</c>): HAREKET-HUD §7's 30 s top-down scenario on a live run with real events and
            /// no frames written - for the acceptance MP4, recorded from outside (tools/player-run.sh + guard): ~10/s bursts to 8
            /// rounds, two empty pulls, a reload, a big hit from the left and a small one 0.6 s later from the upper right, LOW
            /// and a heal, four coin credits 0.1–0.3 s apart, a round change through its transition. Logs a line per beat.
            /// </summary>
            private IEnumerator HudV4Demo()
            {
                yield return HudV4Run();
                RunHudScreen hud = FindObjectOfType<RunHudScreen>();
                PlayerRig rig = PlayerRegistry.Primary;
                WeaponController weapon = WeaponRegistry.ActiveController;
                ClassicWeaponRuntime classic = weapon != null ? weapon.Classic : null;
                if (hud == null || rig == null || rig.Health == null || classic == null)
                {
                    Debug.LogWarning("[UiCapture] hudv4demo: no HUD / player / weapon");
                    yield break;
                }

                PlayerHealth health = rig.Health;
                Vector3 at = rig.transform.position;
                string folder = Path.HasExtension(_out) ? Path.GetDirectoryName(_out) : _out;
                string videoRoot = Arg("-ddTdCapture");
                // The existing async capture agent owns video/audio, not a second recorder. Wait for its t0 anchor.
                if (!string.IsNullOrEmpty(videoRoot))
                    yield return WaitFor(() => File.Exists(Path.Combine(videoRoot, "video", "sync.json")), 30f);
                float began = Time.realtimeSinceStartup;
                // Evidence source cadence only; no persistent settings or simulation clock change.
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 120;
                var probe = gameObject.AddComponent<HudEvidenceProbe>();
                probe.Begin(hud, folder);
                probe.Mark("start", "production HUD events; real-time capture; no fixed-clock playback");
                Debug.Log("[UiCapture] hudv4demo t=0 calm");
                yield return new WaitForSecondsRealtime(2.5f);
                probe.Mark("A1,A2", "shot accents and casing sequence");
                for (int burst = 0; burst < 3; burst++)
                {
                    for (int shot = 0; shot < 7 && classic.Ammo > 8; shot++)
                    {
                        classic.ConsumeShot();
                        yield return new WaitForSecondsRealtime(0.1f);
                    }

                    yield return new WaitForSecondsRealtime(0.5f);
                }

                probe.Mark("A5", "low-ammo hold before empty");
                yield return new WaitForSecondsRealtime(1.2f);
                while (classic.Ammo > 0)
                {
                    classic.ConsumeShot();
                    yield return new WaitForSecondsRealtime(0.1f);
                }

                Debug.Log("[UiCapture] hudv4demo empty pulls");
                yield return new WaitForSecondsRealtime(0.3f);
                probe.Mark("A6", "first empty pull");
                HudV4DryFire();
                yield return new WaitForSecondsRealtime(0.4f);
                HudV4DryFire();
                yield return new WaitForSecondsRealtime(0.3f);
                probe.Mark("A3", "reload started");
                classic.TryBeginReload();
                yield return WaitFor(() => !classic.IsReloading, 6f);
                probe.Mark("A4", "reload completed");
                yield return new WaitForSecondsRealtime(0.4f);
                ClassicWeaponId home = classic.Id;
                ClassicWeaponId other = home == ClassicWeaponId.CombatShotgun ? ClassicWeaponId.AssaultRifle : ClassicWeaponId.CombatShotgun;
                probe.Mark("A7", "EquipClassic " + other);
                weapon.EquipClassic(other);
                yield return new WaitForSecondsRealtime(1.2f);
                weapon.EquipClassic(home);
                yield return new WaitForSecondsRealtime(0.8f);

                probe.Mark("H1,H2,D1,D2", "left hit 30%; upper-right hit follows in .6s");
                Debug.Log("[UiCapture] hudv4demo hits");
                yield return HudV4Hit(health, at, Vector3.left, 0.3f);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return HudV4Hit(health, at, (Vector3.right + Vector3.forward).normalized, 0.08f);
                yield return new WaitForSecondsRealtime(1.8f);
                probe.Mark("H5", "enter LOW with real damage");
                yield return HudV4Hit(health, at, Vector3.back, 0.4f);
                yield return new WaitForSecondsRealtime(3f);
                probe.Mark("H4", "heal 20% through PlayerHealth");
                health.Heal(health.Max * 0.2f);
                yield return new WaitForSecondsRealtime(2f);

                health.HealToFull();
                yield return new WaitForSecondsRealtime(2f); // leave danger posture so G4 is not intentionally suppressed
                probe.Mark("G1,G3,G4", "wallet credits; no fabricated flight");
                Debug.Log("[UiCapture] hudv4demo gold");
                if (GoldService.Instance != null)
                {
                    foreach (float gap in HudV4CoinGaps)
                    {
                        GoldService.Instance.Wallet.Credit(5, at);
                        yield return new WaitForSecondsRealtime(gap);
                    }
                }

                yield return new WaitForSecondsRealtime(2f);
                Debug.Log("[UiCapture] hudv4demo round");
                probe.Mark("R1,R2", "CaptureRound IV; R3 cue is observed separately");
                hud.CaptureRound(4);
                yield return new WaitForSecondsRealtime(3.5f);
                health.HealToFull();
                while (Time.realtimeSinceStartup - began < 35f) yield return null;
                probe.End();
                // Let TdCaptureAgent complete and drain its 40 s window; it quits the Player itself.
                if (!string.IsNullOrEmpty(videoRoot))
                    yield return WaitFor(() => File.Exists(Path.Combine(videoRoot, "video", "summary.json")), 20f);
                Debug.Log("[UiCapture] hudv4demo done · rm=" + UiMotion.ReducedMotion);
            }

            /// <summary>AD M1: real rendered plate over black/white, plus XIV over world and B1; no alpha inferred from a neighbouring world patch.</summary>
            private IEnumerator HudV4Plates()
            {
                string folder = Path.HasExtension(_out) ? Path.GetDirectoryName(_out) : _out;
                Directory.CreateDirectory(folder);
                yield return HudV4Run();
                RunHudScreen hud = FindObjectOfType<RunHudScreen>();
                if (hud == null) yield break;
                RunHudScreen.CaptureParallax = Vector2.zero;
                foreach (int round in HudV4Rounds)
                {
                    hud.CaptureRound(round);
                    yield return WaitFor(() => !hud.RoundTransitionLive, 4f);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return HudV4Frame(folder, "v4-round-" + RomanNumerals.Cached(round), hud);
                }
                hud.CaptureRound(14);
                yield return WaitFor(() => !hud.RoundTransitionLive, 4f);
                yield return new WaitForSecondsRealtime(1f);
                yield return HudV4Frame(folder, "plate-world-XIV", hud);
                Canvas canvas = UiFactory.CreateCanvas("[PlateEvidenceBackground]", UiTokens.SortingHud - 1);
                UnityEngine.UI.Image back = UiFactory.NewImage("Back", canvas.transform, UiColor.SurfaceInset);
                UiFactory.Fill(back.rectTransform);
                hud.RoundNumeral.Box.gameObject.SetActive(false);
                hud.RoundSeal.gameObject.SetActive(false);
                back.color = Color.black;
                yield return null;
                yield return HudV4Frame(folder, "plate-black-XIV", hud);
                back.color = Color.white;
                yield return null;
                yield return HudV4Frame(folder, "plate-white-XIV", hud);
                hud.RoundNumeral.Box.gameObject.SetActive(true);
                hud.RoundSeal.gameObject.SetActive(true);
                back.color = new Color32(216, 210, 196, 255);
                yield return null;
                yield return HudV4Frame(folder, "plate-B1-XIV", hud);
                Destroy(canvas.gameObject);
                RunHudScreen.CaptureParallax = null;
            }

            private static readonly float[] HudV4CoinGaps = { 0.1f, 0.3f, 0.2f, 0.15f };

            /// <summary>Selection → run (the capture weapon flag honoured), live, the player invulnerable between staged hits.</summary>
            private IEnumerator HudV4Run()
            {
                PrototypeUiFlow.GoToSelection();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Selection, 20f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                ApplyCaptureWeapon();
                PrototypeUiFlow.StartRun();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Run, 30f);
                yield return UiCaptureAgent.WaitRunLive(20f);
                PlayerRig rig = PlayerRegistry.Primary;
                if (rig != null && rig.Health != null)
                {
                    rig.Health.DiagnosisInvulnerable = true;
                }
            }

            /// <summary>One real hit of <paramref name="share"/> of max health from <paramref name="towards"/> (the attacker's side).</summary>
            private static IEnumerator HudV4Hit(PlayerHealth health, Vector3 at, Vector3 towards, float share)
            {
                health.DiagnosisInvulnerable = false;
                health.ApplyDamage(health.Max * share, at + towards * 2f, "ui-capture");
                health.DiagnosisInvulnerable = true;
                yield return null;
            }

            private static IEnumerator HudV4Frame(string folder, string name, RunHudScreen hud)
            {
                yield return Frame(folder, name);
                Debug.Log("[UiCapture] hudv4 " + name + " chunks=" + hud.HealthChunksLive + " beats=" + hud.HeartBeats + " glows=" + hud.HealGlows +
                          " accents=" + hud.ShotAccents + " casings=" + hud.CasingsLive + " shakes=" + hud.DryFireShakes + " swaps=" + hud.WeaponSwaps +
                          " punches=" + hud.GoldPunches + " round=" + hud.RoundNumeral.Value + " danger=" + hud.InDangerPosture +
                          // 08.10 (AD MUST-1 / MUST-2 / SHOULD-3): the plate's numbers for the crops' table - cap, plate width, seal
                          // ring diameter, the ink core's alpha and rect (ref px) under the letters.
                          " cap=" + hud.RoundCap.ToString("0.00", CultureInfo.InvariantCulture) +
                          " plate=" + hud.RoundPlateWidth.ToString("0.0", CultureInfo.InvariantCulture) +
                          " seal=" + (hud.RoundSeal != null ? hud.RoundSeal.sizeDelta.x : 0f).ToString("0.0", CultureInfo.InvariantCulture) +
                          " core=" + (hud.RoundCore != null && hud.RoundCore.enabled
                              ? HudArt.CoreAlpha.ToString("0.00", CultureInfo.InvariantCulture) + "@" + hud.RoundCore.rectTransform.sizeDelta.x.ToString("0", CultureInfo.InvariantCulture) + "x" + hud.RoundCore.rectTransform.sizeDelta.y.ToString("0", CultureInfo.InvariantCulture)
                              : "off"));
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
                yield return ArmsPair(folder, "arms-idle");   // FP arms legibility gate (C-00 25.09): HUD on / off, frozen
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
                yield return ArmsPair(folder, "arms-both-low");   // FP arms legibility gate (C-00 25.09): HUD on / off, frozen
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
                Canvas screen = hud.GetComponentInChildren<Canvas>();   // the screen's canvas: the rects' space (centre origin)
                if (screen == null)
                {
                    yield break;
                }

                Vector2 canvas = ((RectTransform)screen.transform).rect.size;
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
                    string how = "placed";
                    if (z == null)
                    {
                        // The camera cannot see a standing head there: pin the stamp's true point on the nearest body.
                        z = Nearest(rig);
                        how = "pinned";
                        UiEnemyStatusMarkers.CaptureZombie = z;
                        UiEnemyStatusMarkers.CaptureTruePoint = point;
                    }

                    if (z == null)
                    {
                        Debug.Log("[UiCapture] " + name + ": no live zombie");
                        continue;
                    }

                    z.ApplyShock(id++, 4f);
                    yield return new WaitForSecondsRealtime(0.12f);
                    yield return Frame(folder, name);
                    Debug.Log("[UiCapture] " + name + " (" + how + ") target=" + target + " point=" + point + " stamp=" + UiEnemyStatusMarkers.LastPlaced);
                    UiEnemyStatusMarkers.CaptureZombie = null;
                    yield return new WaitForSecondsRealtime(0.5f);
                }
            }

            private static ZombieEnemy Nearest(PlayerRig rig)
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

                return pick;
            }

            /// <summary>Moves the live zombie nearest the player so its stamp anchor projects to <paramref name="viewport"/>.</summary>
            private static ZombieEnemy PlaceUnder(Camera camera, Vector3 viewport, PlayerRig rig)
            {
                ZombieEnemy pick = Nearest(rig);
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
                yield return ArmsPair(folder, "arms-special");   // FP arms legibility gate (C-00 25.09): HUD on / off, frozen

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
            /// DEC-107: <c>-ddUiCapture latebonus</c> - the round-end shop's intro at R9 (the bonus announced), R10 (the bonus),
            /// R13 (its last round) and R14 (back to the usual count). The rounds are completed by the spawn director's debug
            /// path, the player is invulnerable and nothing is bought; frames shop-r9 / r10 / r13 / r14 into -ddUiCaptureOut.
            /// </summary>
            private IEnumerator LateBonusFrames()
            {
                UpgradeScreen.CaptureShowRoundReport = Arg("-ddUiRoundReport") == "1";   // DEC-113 frames without buying the node
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
                if (run == null || run.Shop == null)
                {
                    Debug.LogWarning("[UiCapture] latebonus: no run");
                    yield break;
                }

                for (int round = 1; round <= 14; round++)
                {
                    yield return WaitFor(() => run.Phase == RunPhase.Fighting, 60f);
                    yield return new WaitForSecondsRealtime(0.5f);
                    DD.Prototype.Spawn.EnemySpawnDirector spawn = DD.Prototype.Spawn.EnemySpawnDirector.Instance;
                    if (spawn == null || !spawn.DebugCompleteRound())
                    {
                        Debug.LogWarning("[UiCapture] latebonus: round " + round + " could not be completed");
                        yield break;
                    }

                    yield return WaitFor(() => run.Phase == RunPhase.Upgrading, 30f);
                    if (round == 9 || round == 10 || round == 13 || round == 14)
                    {
                        yield return new WaitForSecondsRealtime(1.4f);
                        Debug.Log("[UiCapture] latebonus r" + round + " limit " + run.Shop.PurchaseLimit + " bonus " + run.Shop.LateRoundBonus);
                        yield return Frame(folder, "shop-r" + round);
                    }

                    run.CloseUpgradeScreen();
                }
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
                        if (UiPerf.Requested)
                        {
                            yield return UiPerf.Measure("tree", folder);
                        }
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

            // ============================================================ WP-081 · the menu's cinematic world

            /// <summary>
            /// WP-081 (C-00 05.10 item 5) · <c>-ddUiCapture cinematic</c>: for each of the five shots at its start, mid-move,
            /// end (before the blend) and blend middle - <c>cine-S0n-tLL.L.png</c> (the menu as the player sees it) and
            /// <c>…-bg.png</c> (the world with the backdrop's scrim, every menu graphic off: the ground the words stand on);
            /// <c>cine-elements.jsonl</c> - each visible menu graphic's rect (px, origin bottom-left), colour, size and font,
            /// with the rail focused on each item in turn (the word beside it moves) - for wp-068s9/tools/cine-contrast.py;
            /// <c>cine-collisions.json</c> - the 0.35 m sphere along every camera path and the line of sight to every target;
            /// <c>cine-report.json</c> - when the world went live and how long the doors waited for it.
            /// </summary>
            private IEnumerator CinematicFrames()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                yield return WaitFor(() => MenuCinematic.Current != null && MenuCinematic.Current.Live && !UiCurtain.IsCovering, 60f);
                MainMenuScreen menu = FindObjectOfType<MainMenuScreen>();
                MenuCinematic cinematic = MenuCinematic.Current;
                WriteCinematicReport(folder, cinematic);
                if (menu == null || cinematic == null || !cinematic.Live)
                {
                    Debug.Log("[UiCapture] cinematic: the world is not live; nothing to measure.");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(Mathf.Max(_delay, MenuCinematic.Blend + 0.4f));   // past the plates' fade
                // Nothing but this script drives the menu while it is measured: a click or key reaching the fullscreen Player
                // (a shared machine) once opened panes and PLAY mid-run - the input modules stay off until the end.
                var modules = new System.Collections.Generic.List<UnityEngine.EventSystems.BaseInputModule>();
                foreach (UnityEngine.EventSystems.BaseInputModule module in FindObjectsOfType<UnityEngine.EventSystems.BaseInputModule>())
                {
                    if (module.enabled)
                    {
                        module.enabled = false;
                        modules.Add(module);
                    }
                }

                WriteCollisions(Path.Combine(folder, "cine-collisions.json"));

                var elements = new System.Text.StringBuilder();
                for (int item = 0; item < menu.Rail.Count; item++)
                {
                    UiInput.Focus(menu.Rail[item].Button);
                    yield return new WaitForSecondsRealtime(0.5f);
                    if (menu == null || menu.PaneOpen)
                    {
                        Debug.LogError("[UiCapture] cinematic: the menu changed under the capture (pane open or scene gone) - this run is invalid.");
                        File.WriteAllText(Path.Combine(folder, "cine-INVALID.txt"), "the menu changed under the capture at focus " + item);
                        yield break;
                    }

                    AppendMenuElements(elements, menu, item);
                }

                UiInput.Focus(menu.Rail[0].Button);
                File.WriteAllText(Path.Combine(folder, "cine-elements.jsonl"), elements.ToString());
                yield return new WaitForSecondsRealtime(0.5f);

                var ic = System.Globalization.CultureInfo.InvariantCulture;
                float[] locals = { 0f, 4.8f, 10.79f, 11.4f };
                for (int shot = 0; shot < MenuCinematic.Shots.Length; shot++)
                {
                    foreach (float local in locals)
                    {
                        MenuCinematic.TimeOverride = shot * MenuCinematic.Slice + local;
                        for (int f = 0; f < 4; f++)
                        {
                            yield return null;
                        }

                        string name = MenuCinematic.Shots[shot].id + "-t" + local.ToString("00.00", ic);
                        if (menu == null || menu.PaneOpen)
                        {
                            Debug.LogError("[UiCapture] cinematic: the menu changed under the capture - this run is invalid.");
                            File.WriteAllText(Path.Combine(folder, "cine-INVALID.txt"), "the menu changed under the capture at " + name);
                            yield break;
                        }

                        yield return Frame(folder, "cine-" + name);
                        var hidden = new System.Collections.Generic.List<UnityEngine.UI.Graphic>();
                        foreach (UnityEngine.UI.Graphic g in menu.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                        {
                            if (g.enabled && !UnderBackdrop(g.transform, menu.transform) && g.name != MenuLook.ReadoutPadName)   // a pad is the words' ground
                            {
                                g.enabled = false;
                                hidden.Add(g);
                            }
                        }

                        yield return Frame(folder, "cine-" + name + "-bg");
                        foreach (UnityEngine.UI.Graphic g in hidden)
                        {
                            if (g != null)
                            {
                                g.enabled = true;
                            }
                        }
                    }
                }

                MenuCinematic.TimeOverride = 0f;
                foreach (UnityEngine.EventSystems.BaseInputModule module in modules)
                {
                    if (module != null)
                    {
                        module.enabled = true;
                    }
                }
            }

            private static bool UnderBackdrop(Transform t, Transform root)
            {
                for (; t != null && t != root; t = t.parent)
                {
                    if (t.name == "Backdrop")
                    {
                        return true;
                    }
                }

                return false;
            }

            private static void AppendMenuElements(System.Text.StringBuilder lines, MainMenuScreen menu, int focus)
            {
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                var corners = new Vector3[4];
                foreach (UnityEngine.UI.Graphic g in menu.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                {
                    float alpha = g.canvasRenderer.GetInheritedAlpha() * g.color.a;
                    if (!g.enabled || alpha <= 0.01f)
                    {
                        continue;
                    }

                    bool backdrop = UnderBackdrop(g.transform, menu.transform);
                    if (backdrop && g.name != "TextSafeScrim" && g.name != "TextSafeFade")
                    {
                        continue;
                    }

                    Canvas canvas = g.canvas;
                    Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                    var text = g as TMPro.TMP_Text;
                    if (text != null && string.IsNullOrEmpty(text.text))
                    {
                        continue;
                    }

                    g.rectTransform.GetWorldCorners(corners);
                    if (text != null)
                    {
                        // A word is measured where its letters are (its drawn bounds), not over its layout box - a sub-line's
                        // box spans the column while its words (and their pad) cover a part of it.
                        text.ForceMeshUpdate();
                        Bounds lb = text.textBounds;
                        corners[0] = text.transform.TransformPoint(lb.min);
                        corners[2] = text.transform.TransformPoint(lb.max);
                    }

                    Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                    Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                    float scale = canvas != null ? canvas.scaleFactor : 1f;
                    // The word's own ink (UiHudInk outline, a glyph's Outline): its colour, for the face-vs-ink measure.
                    Color ink = Color.clear;
                    if (text != null && text.fontSharedMaterial != null && text.fontSharedMaterial.HasProperty("_OutlineWidth")
                        && text.fontSharedMaterial.IsKeywordEnabled("OUTLINE_ON") && text.fontSharedMaterial.GetFloat("_OutlineWidth") > 0.01f)
                    {
                        ink = text.fontSharedMaterial.GetColor("_OutlineColor");
                    }
                    else if (text != null && text.fontSharedMaterial != null && text.fontSharedMaterial.IsKeywordEnabled("UNDERLAY_ON")
                        && text.fontSharedMaterial.HasProperty("_UnderlayColor"))
                    {
                        ink = text.fontSharedMaterial.GetColor("_UnderlayColor");   // MenuLook's small-text ink: a ground, not an edge
                    }
                    else if (text == null && g.GetComponent<UnityEngine.UI.Outline>() != null)
                    {
                        ink = g.GetComponent<UnityEngine.UI.Outline>().effectColor;
                    }

                    // The focused word's pad moves with the word (the bg frames hold it at PLAY's row): report it, so a word
                    // measured at another row is judged on its pad too.
                    Color movingPad = Color.clear;
                    Transform padT = text != null && text.transform.parent != null && text.transform.parent.name == "RailWord"
                        ? text.transform.parent.Find(MenuLook.ReadoutPadName) : null;
                    if (padT != null && padT.gameObject.activeInHierarchy && padT.GetComponent<UnityEngine.UI.Graphic>() != null)
                    {
                        movingPad = padT.GetComponent<UnityEngine.UI.Graphic>().color;
                    }

                    string pathName = g.name;
                    for (Transform t = g.transform.parent; t != null && t != menu.transform; t = t.parent)
                    {
                        pathName = t.name + "/" + pathName;
                    }

                    Color c = g.color;
                    lines.Append(string.Format(ic,
                        "{{\"focus\":{0},\"path\":\"{1}\",\"text\":{2},\"words\":\"{3}\",\"font\":\"{4}\",\"x0\":{5:F1},\"y0\":{6:F1},\"x1\":{7:F1},\"y1\":{8:F1},\"r\":{9:F4},\"g\":{10:F4},\"b\":{11:F4},\"a\":{12:F4},\"sizePx\":{13:F1},\"bold\":{14},\"backdrop\":{15},\"screen\":[{16},{17}],\"ink\":[{18:F4},{19:F4},{20:F4},{21:F4}],\"movingPad\":[{22:F4},{23:F4},{24:F4},{25:F4}] }}\n",
                        focus, pathName.Replace("\"", "'"), text != null ? "true" : "false",
                        text != null ? text.text.Replace("\"", "'").Replace("\n", " ") : string.Empty,
                        text != null && text.font != null ? text.font.name.Replace("\"", "'") : string.Empty,
                        Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y),
                        c.r, c.g, c.b, alpha, text != null ? text.fontSize * scale : 0f,
                        text != null && (text.fontStyle & TMPro.FontStyles.Bold) != 0 ? "true" : "false",
                        backdrop ? "true" : "false", Screen.width, Screen.height, ink.r, ink.g, ink.b, ink.a, movingPad.r, movingPad.g, movingPad.b, movingPad.a));
                }
            }

            private static void WriteCinematicReport(string folder, MenuCinematic cinematic)
            {
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                DD.Prototype.World.WorldSource source = DD.Prototype.World.WorldSourceSettings.Resolve(out string scene);
                string json = string.Format(ic,
                    "{{\"live\":{0},\"world\":\"{1}\",\"source\":\"{2}\",\"viewer\":{3},\"overlayScreens\":{4},\"doorsWaitSeconds\":{5:F3},\"shots\":{6},\"periodSeconds\":{7:F1},\"screen\":[{8},{9}],\"reducedMotion\":{10}}}",
                    cinematic != null && cinematic.Live ? "true" : "false", scene, source, DD.Prototype.World.WorldViewer.Active ? "true" : "false",
                    UiFactory.OverlayScreens ? "true" : "false", PrototypeUiFlow.LastMenuWorldWaitSeconds, MenuCinematic.Shots.Length,
                    MenuCinematic.Period, Screen.width, Screen.height, UiMotion.ReducedMotion ? "true" : "false");
                File.WriteAllText(Path.Combine(folder, "cine-report.json"), json);
                Debug.Log("[UiCapture] cinematic " + json);
            }

            /// <summary>
            /// AP-028 runtime acceptance: the camera's 0.35 m sphere along every path (61 samples, colliders only) and the
            /// renderers whose bounds hold a sample (foliage and eaves carry no collider - the frames judge those), and the
            /// line of sight from each pose to its target (the first collider in the way and how far short of the target).
            /// </summary>
            private static void WriteCollisions(string path)
            {
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                var json = new System.Text.StringBuilder("{\"radius\":0.35,\"shots\":[");
                int mask = ~(1 << 5);
                Renderer[] renderers = FindObjectsOfType<Renderer>();
                MenuCinematic.Shot[] shots = MenuCinematic.Shots;
                for (int s = 0; s < shots.Length; s++)
                {
                    var hits = new System.Collections.Generic.SortedDictionary<string, int>();
                    var inside = new System.Collections.Generic.SortedDictionary<string, int>();
                    for (int k = 0; k <= 60; k++)
                    {
                        Vector3 p = Vector3.Lerp(shots[s].Start, shots[s].End, k / 60f);
                        foreach (Collider c in Physics.OverlapSphere(p, 0.35f, mask, QueryTriggerInteraction.Ignore))
                        {
                            string n = c.name.Replace("\"", "'");
                            hits[n] = hits.TryGetValue(n, out int h) ? h + 1 : 1;
                        }

                        foreach (Renderer r in renderers)
                        {
                            if (r != null && r.enabled && r.bounds.size.magnitude < 12f && r.bounds.SqrDistance(p) < 0.35f * 0.35f)
                            {
                                string n = r.name.Replace("\"", "'");
                                inside[n] = inside.TryGetValue(n, out int h) ? h + 1 : 1;
                            }
                        }
                    }

                    json.Append(s > 0 ? "," : string.Empty).Append("{\"id\":\"").Append(shots[s].id).Append("\",\"colliderHits\":{");
                    int i = 0;
                    foreach (var kv in hits)
                    {
                        json.Append(i++ > 0 ? "," : string.Empty).Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
                    }

                    json.Append("},\"nearRenderers\":{");
                    i = 0;
                    foreach (var kv in inside)
                    {
                        json.Append(i++ > 0 ? "," : string.Empty).Append('"').Append(kv.Key).Append("\":").Append(kv.Value);
                    }

                    json.Append("},\"sight\":[");
                    Vector3[] from = { shots[s].Start, shots[s].End };
                    Vector3[] to = { shots[s].TargetStart, shots[s].TargetEnd };
                    for (int e = 0; e < 2; e++)
                    {
                        Vector3 d = to[e] - from[e];
                        bool blocked = Physics.Raycast(from[e], d.normalized, out RaycastHit hit, d.magnitude, mask, QueryTriggerInteraction.Ignore);
                        json.Append(e > 0 ? "," : string.Empty).Append(string.Format(ic, "{{\"end\":\"{0}\",\"distance\":{1:F2},\"blocked\":{2},\"hit\":\"{3}\",\"hitDistance\":{4:F2} }}",
                            e == 0 ? "start" : "end", d.magnitude, blocked ? "true" : "false", blocked ? hit.collider.name.Replace("\"", "'") : string.Empty, blocked ? hit.distance : 0f));
                    }

                    json.Append("]}");
                }

                json.Append("]}");
                File.WriteAllText(path, json.ToString());
                Debug.Log("[UiCapture] cinematic collisions " + path);
            }

            /// <summary>
            /// WP-081 (C-00 05.10 item 4) · <c>-ddUiCapture cinematicperf</c> [<c>-ddCinePerfSeconds 120</c>]: the menu as it
            /// runs (two loops by default; with <c>-ddMenuWorld off</c> the plates - the baseline) with the frame cap and
            /// vsync lifted: frame, CPU main-thread and GPU ms (avg / p50 / p95 / p99 / max), SetPass / batches / draws
            /// (avg / max), GC bytes per frame, and the worst frame inside the blends against the rest -
            /// <c>cine-perf.json</c> and one <c>[CinePerf]</c> line.
            /// </summary>
            private IEnumerator CinematicPerf()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                yield return WaitFor(() => FindObjectOfType<MainMenuScreen>() != null && !UiCurtain.IsCovering, 60f);
                yield return WaitFor(() => MenuCinematic.Current == null || MenuCinematic.Current.Live, 30f);
                MenuCinematic.TimeOverride = null;
                yield return new WaitForSecondsRealtime(Mathf.Max(_delay, 2f));
                float seconds;
                if (!float.TryParse(Arg("-ddCinePerfSeconds"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds) || seconds <= 0f)
                {
                    seconds = 120f;
                }

                int capBefore = Application.targetFrameRate;
                int vsyncBefore = QualitySettings.vSyncCount;
                Application.targetFrameRate = -1;
                QualitySettings.vSyncCount = 0;
                ProfilerRecorder setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                ProfilerRecorder batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                ProfilerRecorder draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                ProfilerRecorder gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                var timings = new FrameTiming[1];
                var frame = new System.Collections.Generic.List<float>(16384);
                var cpu = new System.Collections.Generic.List<float>(16384);
                var gpu = new System.Collections.Generic.List<float>(16384);
                double setPassSum = 0d, batchSum = 0d, drawSum = 0d, gcSum = 0d;
                long setPassMax = 0, batchMax = 0, drawMax = 0;
                float blendWorst = 0f, restWorst = 0f;
                int n = 0;
                yield return null;
                yield return null;
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < seconds)
                {
                    yield return null;
                    float ms = Time.unscaledDeltaTime * 1000f;
                    frame.Add(ms);
                    n++;
                    MenuCinematic c = MenuCinematic.Current;
                    bool blend = false;
                    if (c != null && c.Live)
                    {
                        MenuCinematic.SliceAt(c.Clock, out float local);
                        blend = MenuCinematic.Blending(local) || local < 0.1f;
                    }

                    if (blend)
                    {
                        blendWorst = Mathf.Max(blendWorst, ms);
                    }
                    else
                    {
                        restWorst = Mathf.Max(restWorst, ms);
                    }

                    long sp = setPass.Valid ? setPass.LastValue : 0, ba = batches.Valid ? batches.LastValue : 0, dr = draws.Valid ? draws.LastValue : 0;
                    setPassSum += sp; batchSum += ba; drawSum += dr; gcSum += gc.Valid ? gc.LastValue : 0;
                    setPassMax = Math.Max(setPassMax, sp); batchMax = Math.Max(batchMax, ba); drawMax = Math.Max(drawMax, dr);
                    FrameTimingManager.CaptureFrameTimings();
                    if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                    {
                        if (timings[0].cpuMainThreadFrameTime > 0d)
                        {
                            cpu.Add((float)timings[0].cpuMainThreadFrameTime);
                        }

                        if (timings[0].gpuFrameTime > 0d)
                        {
                            gpu.Add((float)timings[0].gpuFrameTime);
                        }
                    }
                }

                Application.targetFrameRate = capBefore;
                QualitySettings.vSyncCount = vsyncBefore;
                setPass.Dispose(); batches.Dispose(); draws.Dispose(); gc.Dispose();
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                string json = string.Format(ic,
                    "{{\"mode\":\"{0}\",\"screen\":\"{1}x{2}\",\"seconds\":{3:F1},\"frames\":{4},\"frameMs\":{5},\"cpuMainMs\":{6},\"gpuMs\":{7}," +
                    "\"setPassAvg\":{8:F1},\"setPassMax\":{9},\"batchesAvg\":{10:F1},\"batchesMax\":{11},\"drawsAvg\":{12:F1},\"drawsMax\":{13},\"gcBytesPerFrame\":{14:F0}," +
                    "\"blendWorstMs\":{15:F2},\"restWorstMs\":{16:F2},\"doorsWaitSeconds\":{17:F3},\"quality\":\"{18}\"}}",
                    MenuCinematic.Current != null && MenuCinematic.Current.Live ? "world" : "plates", Screen.width, Screen.height, seconds, n,
                    Stats(frame), Stats(cpu), Stats(gpu), setPassSum / Math.Max(1, n), setPassMax, batchSum / Math.Max(1, n), batchMax,
                    drawSum / Math.Max(1, n), drawMax, gcSum / Math.Max(1, n), blendWorst, restWorst, PrototypeUiFlow.LastMenuWorldWaitSeconds,
                    QualitySettings.names[QualitySettings.GetQualityLevel()]);
                File.WriteAllText(Path.Combine(folder, "cine-perf.json"), json);
                Debug.Log("[CinePerf] " + json);
            }

            private static string Stats(System.Collections.Generic.List<float> values)
            {
                if (values.Count == 0)
                {
                    return "null";
                }

                values.Sort();
                double sum = 0d;
                foreach (float v in values)
                {
                    sum += v;
                }

                float P(float q) => values[Math.Min(values.Count - 1, (int)(values.Count * q))];
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{{\"n\":{0},\"avg\":{1:F3},\"p50\":{2:F3},\"p95\":{3:F3},\"p99\":{4:F3},\"max\":{5:F3} }}",
                    values.Count, sum / values.Count, P(0.5f), P(0.95f), P(0.99f), values[values.Count - 1]);
            }

            /// <summary>
            /// WP-081 (C-00 05.10: "ilk çalışan sürümde 60 s MP4") · <c>-ddUiCapture cinematicmp4</c>: one loop at 30 fps on a
            /// fixed clock (Time.captureDeltaTime 1/30 and the loop time per frame), the menu over it, as numbered JPEG
            /// frames (q 92) in <c>frames/</c> - wp-068s9/tools/frames2mp4 turns them into the MP4.
            /// </summary>
            private IEnumerator CinematicMp4()
            {
                string folder = Path.Combine(Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out, "frames");
                Directory.CreateDirectory(folder);
                yield return WaitFor(() => MenuCinematic.Current != null && MenuCinematic.Current.Live && !UiCurtain.IsCovering, 60f);
                if (MenuCinematic.Current == null || !MenuCinematic.Current.Live)
                {
                    Debug.Log("[UiCapture] cinematicmp4: the world is not live.");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(Mathf.Max(_delay, MenuCinematic.Blend + 0.4f));
                // As CinematicFrames: nothing but this script drives the menu while the loop is recorded.
                foreach (UnityEngine.EventSystems.BaseInputModule module in FindObjectsOfType<UnityEngine.EventSystems.BaseInputModule>())
                {
                    module.enabled = false;
                }

                MainMenuScreen menu = FindObjectOfType<MainMenuScreen>();
                const int fps = 30;
                int count = Mathf.RoundToInt(MenuCinematic.Period * fps);
                Time.captureDeltaTime = 1f / fps;
                // The loop's last frames first, not recorded: frame 0 then follows them as in the game (straight after the jump to
                // t = 0 it differed from frame 1 4-6× a shot cut - the video check's loop seam, 06.10).
                for (int w = count - 3; w < count; w++)
                {
                    MenuCinematic.TimeOverride = w / (float)fps;
                    yield return new WaitForEndOfFrame();
                }

                for (int i = 0; i < count; i++)
                {
                    MenuCinematic.TimeOverride = i / (float)fps;
                    yield return new WaitForEndOfFrame();
                    if (menu == null || menu.PaneOpen)
                    {
                        Debug.LogError("[UiCapture] cinematicmp4: the menu changed under the recording - invalid.");
                        File.WriteAllText(Path.Combine(folder, "..", "cine-INVALID.txt"), "the menu changed under the recording at frame " + i);
                        break;
                    }

                    Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
                    File.WriteAllBytes(Path.Combine(folder, "frame-" + i.ToString("00000") + ".jpg"), shot.EncodeToJPG(92));
                    Destroy(shot);
                }

                Time.captureDeltaTime = 0f;
                MenuCinematic.TimeOverride = 0f;
                Debug.Log("[UiCapture] cinematicmp4 " + count + " frames in " + folder);
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

            /// <summary>AP-035 R3: the loading logo's intro shots, seconds after the loading group shows.</summary>
            private static readonly float[] LogoIntroShots = { 0.5f, 1.0f, 1.2f, 1.44f, 1.75f };

            /// <summary>A frame with the logo's state at the capture (phase, spec time, alpha) in the log.</summary>
            private static IEnumerator LogoFrame(string folder, string name, UiLogoAnimator logo)
            {
                yield return new WaitForEndOfFrame();
                if (logo != null)
                {
                    float now = UiFxGlobals.Now;
                    Debug.Log("[UiCapture] " + name + " logo phase=" + logo.Phase + " spec=" + logo.Timeline.SpecTime(now).ToString("F3")
                              + " frame=" + UiLogoTimeline.Frame(logo.Timeline.SpecTime(now)).ToString("F1") + " alpha=" + logo.Alpha.ToString("F2"));
                }

                WritePng(Path.Combine(folder, name + ".png"));
            }

            /// <summary>
            /// AP-035 R3 (K7): <c>-ddUiCapture menuintro</c> - the main menu's logo intro after the boot (once per session):
            /// shots at 0.5 / 1.0 / 1.44 (impact) / 1.75 / 1.9 s of the intro, then the menu with its title after it.
            /// </summary>
            private IEnumerator MenuIntroFrames()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                yield return WaitFor(() => UiLogoMenuIntro.Current != null && UiLogoMenuIntro.Current.Running, 40f);
                UiLogoMenuIntro intro = UiLogoMenuIntro.Current;
                if (intro == null)
                {
                    Debug.Log("[UiCapture] menuintro: no intro ran (batch / reduced motion / FastTransitions?)");
                    Application.Quit();
                    yield break;
                }

                float t0 = intro.Logo.Timeline.IntroStart;
                foreach (float at in new[] { 0.5f, 1.0f, 1.44f, 1.75f, 1.9f })
                {
                    yield return WaitFor(() => UiFxGlobals.Now - t0 >= at, 5f);
                    yield return LogoFrame(folder, "menu-intro-t" + Mathf.RoundToInt(at * 100f).ToString("000"), intro.Logo);
                    Debug.Log("[UiCapture] menu-intro-t" + Mathf.RoundToInt(at * 100f).ToString("000") + " title=" + intro.TitleAlpha.ToString("F2"));
                }

                yield return WaitFor(() => UiLogoMenuIntro.Current == null, 5f);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Frame(folder, "menu-intro-after");
                Debug.Log("[UiCapture] menuintro done · framebuffer " + Screen.width + "x" + Screen.height);
                Application.Quit();
            }

            /// <summary>
            /// The FP arms legibility gate (C-00 25.09: the HUD draws over the viewmodel's arms): the world frozen
            /// (timeScale 0), one frame with the HUD (<c>-hud</c>), one without it (<c>-bare</c>: every canvas under the
            /// RunHudScreen off), one without the HUD and the FP arms (<c>-noarms</c>: bare vs noarms = the arm mask), and
            /// <c>-elements.jsonl</c> - each visible HUD graphic's frame rect (px, origin bottom-left),
            /// colour, size and ink edge - so tools/fparms-contrast.py can measure each element against the pixels under it.
            /// First person only (the arms are the FP viewmodel's).
            /// </summary>
            private static IEnumerator ArmsPair(string folder, string name)
            {
                RunHudScreen hud = UnityEngine.Object.FindObjectOfType<RunHudScreen>();
                if (hud == null || DD.Prototype.CameraSystem.CameraModeService.Current != DD.Prototype.CameraSystem.CameraMode.FirstPerson)
                {
                    yield break;
                }

                float timeScale = Time.timeScale;
                Time.timeScale = 0f;
                yield return null;
                yield return null;
                yield return Frame(folder, name + "-hud");
                DumpHudElements(Path.Combine(folder, name + "-elements.jsonl"), hud);
                Canvas[] canvases = hud.GetComponentsInChildren<Canvas>(false);
                var was = new bool[canvases.Length];
                for (int i = 0; i < canvases.Length; i++)
                {
                    was[i] = canvases[i].enabled;
                    canvases[i].enabled = false;
                }

                yield return null;
                yield return Frame(folder, name + "-bare");

                // The arm mask: the same frame with the FP arms' renderers off (the weapon stays) - bare vs noarms = arm pixels.
                // FirstPersonArms instantiates its model at the scene root as "FP_Arms" (S-06's file, read only here).
                GameObject arms = DD.Prototype.Characters.FirstPersonArms.Shown != null ? GameObject.Find("FP_Arms") : null;
                Renderer[] armRenderers = arms != null ? arms.GetComponentsInChildren<Renderer>(false) : new Renderer[0];
                var armWas = new bool[armRenderers.Length];
                for (int i = 0; i < armRenderers.Length; i++)
                {
                    armWas[i] = armRenderers[i].enabled;
                    armRenderers[i].enabled = false;
                }

                yield return null;
                yield return Frame(folder, name + "-noarms");
                for (int i = 0; i < armRenderers.Length; i++)
                {
                    armRenderers[i].enabled = armWas[i];
                }

                Debug.Log("[UiCapture] " + name + ": arms " + (arms != null ? armRenderers.Length + " renderers" : "none shown"));
                for (int i = 0; i < canvases.Length; i++)
                {
                    canvases[i].enabled = was[i];
                }

                Time.timeScale = timeScale;
                yield return null;
            }

            private static void DumpHudElements(string path, RunHudScreen hud)
            {
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                var lines = new System.Text.StringBuilder();
                var corners = new Vector3[4];
                foreach (UnityEngine.UI.Graphic g in hud.GetComponentsInChildren<UnityEngine.UI.Graphic>(false))
                {
                    if (!g.enabled || g.canvasRenderer.GetInheritedAlpha() * g.color.a <= 0.01f)
                    {
                        continue;
                    }

                    Canvas canvas = g.canvas;
                    Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                    g.rectTransform.GetWorldCorners(corners);
                    Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                    Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                    var text = g as TMPro.TMP_Text;
                    bool ink = false;
                    float size = 0f;
                    string words = string.Empty;
                    if (text != null)
                    {
                        Material m = text.fontSharedMaterial;
                        ink = m != null && m.HasProperty("_OutlineWidth") && m.GetFloat("_OutlineWidth") > 0.01f;
                        size = text.fontSize;
                        words = text.text ?? string.Empty;
                    }
                    else
                    {
                        ink = g.GetComponent<UiFx>() != null;
                    }

                    Color c = g.color;
                    float alpha = g.canvasRenderer.GetInheritedAlpha() * c.a;
                    string pathName = g.name;
                    for (Transform t = g.transform.parent; t != null && t != hud.transform; t = t.parent)
                    {
                        pathName = t.name + "/" + pathName;
                    }

                    lines.Append(string.Format(ic,
                        "{{\"path\":\"{0}\",\"text\":{1},\"words\":\"{2}\",\"x0\":{3:F1},\"y0\":{4:F1},\"x1\":{5:F1},\"y1\":{6:F1},\"r\":{7:F4},\"g\":{8:F4},\"b\":{9:F4},\"a\":{10:F4},\"size\":{11:F1},\"ink\":{12}}}\n",
                        pathName.Replace("\"", "'"), text != null ? "true" : "false", words.Replace("\"", "'").Replace("\n", " "),
                        Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y),
                        c.r, c.g, c.b, alpha, size, ink ? "true" : "false"));
                }

                File.WriteAllText(path, lines.ToString());
                Debug.Log("[UiCapture] hud elements " + path);
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
            /// <summary>WP-081 menu v4: the arrival (every pane closed), the rail's word moved to SETTINGS, the Profile pane open.</summary>
            private IEnumerator MenuV4Frames()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                Directory.CreateDirectory(folder);
                yield return WaitFor(() => FindObjectOfType<MainMenuScreen>() != null && !UiCurtain.IsCovering, 30f);
                yield return new WaitForSecondsRealtime(_delay);
                MainMenuScreen menu = FindObjectOfType<MainMenuScreen>();
                yield return Frame(folder, "menu-arrival");
                if (menu == null || menu.Rail.Count < 8)
                {
                    yield break;
                }

                UiInput.Focus(menu.Rail[4].Button);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Frame(folder, "menu-focus-settings");
                menu.ShowPane(UiContext.Profile);
                UiInput.Focus(menu.Rail[1].Button);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Frame(folder, "menu-pane-profile");
            }

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
#endif
