#if DEVELOPMENT_BUILD || UNITY_EDITOR
// WP-098B (S-04, C-00 07.10): the brush windows' visual evidence - dev builds and the Editor only, as the rest of the agent.
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Economy;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    public static partial class UiCaptureAgent
    {
        private sealed partial class Driver
        {
            private bool _recording;
            private GoldEvidenceTrace _goldEvidence;
            private readonly List<string> _windowStills = new List<string>();
            private readonly List<string> _windowSegments = new List<string>();

            /// <summary>
            /// WP-098B · <c>-ddUiCapture windows</c> (with <c>-ddUiLanguage tr</c> for the Turkish pass): one launch walks every
            /// window the player reaches, as a player would (focus moves, pane opens, tab presses, Cancel), and writes
            /// <c>stills/NN-name.png</c> plus three real-time recordings - <c>video/nav-menu</c>, <c>video/selection</c> and
            /// <c>video/nav-ingame</c> - as JPEG frames with a <c>frames.tsv</c> ("idx TAB seconds") that td-capture's tdmux lays
            /// on a constant 30 fps grid. Real time, not captureDeltaTime: UI motion runs on unscaled time.
            /// The run is the Player's: pass a sandbox -ddProfilePath / -ddUiDataRoot and keep it inside the guard window.
            /// </summary>
            private IEnumerator WindowsFrames()
            {
                string folder = Path.HasExtension(_out) ? (Path.GetDirectoryName(_out) ?? Application.persistentDataPath) : _out;
                string stills = Path.Combine(folder, "stills");
                Directory.CreateDirectory(stills);
                // S-04 G-M3: a narrow retake of the real gold chain; no unchanged menu/selection tour.
                if (Arg("-ddUiWindowsGoldOnly") == "1")
                {
                    yield return GoldEvidenceFrames(folder, stills);
                    yield break;
                }
                yield return WaitFor(() => FindObjectOfType<MainMenuScreen>() != null && !UiCurtain.IsCovering, 60f);
                yield return new WaitForSecondsRealtime(_delay);
                MainMenuScreen menu = FindObjectOfType<MainMenuScreen>();
                if (menu == null)
                {
                    Debug.LogError("[UiCapture] windows: no main menu");
                    yield break;
                }

                // --- the menu and its panes ---------------------------------------------------------------------------
                yield return Still(stills, "01-menu");
                StartCoroutine(Record(folder, "nav-menu"));
                for (int i = 0; i < menu.Rail.Count; i++)
                {
                    UiInput.Focus(menu.Rail[i].Button);
                    yield return new WaitForSecondsRealtime(0.45f);
                }

                yield return Pane(menu, UiContext.Profile, stills, "02-profile");
                yield return Pane(menu, UiContext.Statistics, stills, "03-statistics");
                yield return Pane(menu, UiContext.Achievements, stills, "04-achievements");
                yield return Pane(menu, UiContext.Settings, stills, "05-settings-display");
                string[] tabs = MainMenuScreen.PaneLook.SettingsTabIds;
                for (int t = 1; t < tabs.Length; t++)
                {
                    Press(MainMenuScreen.PaneLook.SettingsTabPrefix + tabs[t]);
                    yield return new WaitForSecondsRealtime(1.1f);
                    yield return Still(stills, (5 + t).ToString("00", CultureInfo.InvariantCulture) + "-settings-" + tabs[t].ToLowerInvariant());
                }

                yield return Pane(menu, UiContext.Controls, stills, "10-controls");
                // AD S9: the controls pane's end (GAMEPAD / RUMBLE) - the last control focused, as a pad reaches it.
                yield return FocusLastInPane();
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Still(stills, "10b-controls-end");
                yield return Pane(menu, UiContext.Accessibility, stills, "11-accessibility");
                UiInput.SimulateCancelNextFrame();
                yield return new WaitForSecondsRealtime(1f);
                PrototypeUiFlow.RequestQuit();
                yield return new WaitForSecondsRealtime(1.1f);
                yield return Still(stills, "12-quit-confirm");
                UiInput.SimulateCancelNextFrame();   // the default answer is No: Cancel keeps the game
                yield return new WaitForSecondsRealtime(1f);
                _recording = false;
                yield return null;
                if (PrototypeUiFlow.QuitConfirmOpen)
                {
                    Debug.LogError("[UiCapture] windows: the quit confirm did not close on Cancel");
                    yield break;
                }

                // --- item 12: PREPARE YOUR RUN with the AP-036 side views ---------------------------------------------------
                PrototypeUiFlow.GoToSelection();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Selection, 20f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                yield return new WaitForSecondsRealtime(1f);
                yield return Still(stills, "13-selection");
                StartCoroutine(Record(folder, "selection"));
                Selectable[] weapons = Selectables("Weapon.");
                float t0 = Time.realtimeSinceStartup;
                for (int w = 0; w < weapons.Length && Time.realtimeSinceStartup - t0 < 9f; w++)
                {
                    UiInput.Focus(weapons[w]);
                    yield return new WaitForSecondsRealtime(0.35f);
                    Submit(weapons[w].gameObject);
                    yield return new WaitForSecondsRealtime(0.85f);
                    if (w == 2)
                    {
                        yield return Still(stills, "14-selection-weapon");
                    }
                }

                if (weapons.Length > 0)
                {
                    Submit(weapons[0].gameObject);
                    yield return new WaitForSecondsRealtime(0.6f);
                }

                _recording = false;
                yield return null;

                // --- in game: pause, END RUN confirm, gold, round complete, results -----------------------------------------
                PrototypeUiFlow.StartRun();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Run, 30f);
                yield return UiCaptureAgent.WaitRunLive(30f);
                yield return new WaitForSecondsRealtime(2f);
                PlayerRig rig = PlayerRegistry.Primary;
                if (rig != null && rig.Health != null)
                {
                    rig.Health.DiagnosisInvulnerable = true;
                }

                StartCoroutine(Record(folder, "nav-ingame"));
                PauseScreen pause = FindObjectOfType<PauseScreen>();
                if (pause != null)
                {
                    pause.SetPaused(true);
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return Still(stills, "15-pause");
                    // AD M1: one step down to the shake slider - the focus corner wraps its row.
                    Selectable shake = pause.ShakeSlider != null ? pause.ShakeSlider.GetComponentInChildren<Selectable>() : null;
                    if (shake != null)
                    {
                        UiInput.Focus(shake);
                        yield return new WaitForSecondsRealtime(0.6f);
                        yield return Still(stills, "15b-pause-shake");
                        if (pause.ResumeButton != null)
                        {
                            UiInput.Focus(pause.ResumeButton);
                            yield return new WaitForSecondsRealtime(0.4f);
                        }
                    }

                    if (pause.MainMenuButton != null)
                    {
                        UiInput.Focus(pause.MainMenuButton);
                        yield return new WaitForSecondsRealtime(0.4f);
                        Submit(pause.MainMenuButton.gameObject);
                        yield return new WaitForSecondsRealtime(1.1f);
                        yield return Still(stills, "16-pause-confirm");
                        UiInput.SimulateCancelNextFrame();   // KEEP PLAYING
                        yield return new WaitForSecondsRealtime(0.8f);
                    }

                    pause.SetPaused(false);
                    yield return new WaitForSecondsRealtime(0.8f);
                }

                // Gold: three kills beside the player (land, magnet, pickup, the HUD flight), three further out (the
                // round end's sweep flight to the HUD) - the real chain, ZombieEnemy.Kill → EnemyDied → GoldService.
                yield return Kills(rig, 2.2f, 3);
                yield return new WaitForSecondsRealtime(2.6f);
                yield return Kills(rig, 9f, 3);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Still(stills, "17-gold-landed");
                RunDirector run = RunDirector.Instance;
                DD.Prototype.Spawn.EnemySpawnDirector spawn = DD.Prototype.Spawn.EnemySpawnDirector.Instance;
                if (run != null && spawn != null && spawn.DebugCompleteRound())
                {
                    yield return WaitFor(() => run.Phase == RunPhase.Upgrading, 30f);
                    // The round tally rolls only when the Round Report line is unlocked (DEC-113); a fresh profile has none,
                    // so the counting still is taken only while it rolls (AD S9: 18 and 19 were the same frame).
                    UpgradeScreen upgrade = null;
                    System.Reflection.FieldInfo rolling = typeof(UpgradeScreen).GetField("_tallyRolling", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    yield return WaitFor(() => (upgrade = FindObjectOfType<UpgradeScreen>()) != null && rolling != null && (bool)rolling.GetValue(upgrade), 1.5f);
                    if (upgrade != null && rolling != null && (bool)rolling.GetValue(upgrade))
                    {
                        yield return new WaitForSecondsRealtime(0.25f);
                        yield return Still(stills, "18-upgrade-counting");
                    }
                    else
                    {
                        Debug.Log("[UiCapture] windows: no round tally rolled (Round Report line locked on this profile) - no counting still");
                    }

                    yield return new WaitForSecondsRealtime(2.5f);
                    yield return Still(stills, "19-upgrade");
                    run.CloseUpgradeScreen();
                    yield return WaitFor(() => run.Phase == RunPhase.Fighting, 40f);
                    yield return new WaitForSecondsRealtime(1f);
                }

                if (rig != null && rig.Health != null)
                {
                    rig.Health.DiagnosisInvulnerable = false;
                    rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, CaptureKilledBy());
                }

                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Results, 30f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 10f);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Still(stills, "20-results-counting");
                yield return new WaitForSecondsRealtime(3f);
                yield return Still(stills, "21-results");
                _recording = false;
                yield return null;

                // --- the load error (a scene that does not exist), from the main menu -------------------------------------
                PrototypeUiFlow.GoToMainMenu();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.MainMenu, 30f);
                yield return WaitFor(() => !UiCurtain.IsCovering, 20f);
                yield return new WaitForSecondsRealtime(1f);
                PrototypeUiFlow.LoadForDiagnostics("DD_R1E_NoSuchScene");
                yield return new WaitForSecondsRealtime(1f);
                yield return Still(stills, "22-load-error");

                var json = new StringBuilder();
                json.Append("{\"mode\":\"windows\",\"language\":\"").Append(UiSettings.Language).Append("\",\"screen\":\"")
                    .Append(Screen.width).Append('x').Append(Screen.height).Append("\",\"stills\":[");
                for (int i = 0; i < _windowStills.Count; i++)
                {
                    json.Append(i == 0 ? "\"" : ",\"").Append(_windowStills[i]).Append('"');
                }

                json.Append("],\"videos\":[");
                for (int i = 0; i < _windowSegments.Count; i++)
                {
                    json.Append(i == 0 ? string.Empty : ",").Append(_windowSegments[i]);
                }

                json.Append("]}");
                File.WriteAllText(Path.Combine(folder, "windows.json"), json.ToString());
                Debug.Log("[UiCapture] windows " + json);
            }

            /// <summary>
            /// -ddUiCapture windows -ddUiWindowsGoldOnly 1: the actual enemy death → packet → magnet → HUD flight and
            /// round-end sweep in one 35 s TD recording. No wallet credits, synthetic Depart/Arrive or test HUD binding.
            /// TSV samples share Record's frame index/time; events also carry the Unity frame/realtime for alignment.
            /// </summary>
            private IEnumerator GoldEvidenceFrames(string folder, string stills)
            {
                yield return WaitFor(() => FindObjectOfType<MainMenuScreen>() != null && !UiCurtain.IsCovering, 60f);
                PrototypeUiFlow.StartRun();
                yield return UiCaptureAgent.WaitScene(PrototypeScenes.Run, 30f);
                yield return UiCaptureAgent.WaitRunLive(30f);
                yield return new WaitForSecondsRealtime(1f);
                RunHudScreen hud = FindObjectOfType<RunHudScreen>();
                GoldService gold = GoldService.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                RunDirector run = RunDirector.Instance;
                DD.Prototype.Spawn.EnemySpawnDirector spawn = DD.Prototype.Spawn.EnemySpawnDirector.Instance;
                UiCounter counter = null;
                if (hud != null)
                {
                    foreach (UiCounter readout in hud.GetComponentsInChildren<UiCounter>(true))
                    {
                        if (readout.transform.parent != null && readout.transform.parent.name == "Gold")
                        {
                            counter = readout;
                            break;
                        }
                    }
                }

                if (hud == null || gold == null || rig == null || rig.Health == null || run == null || spawn == null || counter == null)
                {
                    File.WriteAllText(Path.Combine(folder, "gold-error.txt"), "Missing real run HUD / gold service / player / director / Gold counter.");
                    yield break;
                }

                bool spawning = spawn.SpawningEnabledDev;
                bool invulnerable = rig.Health.DiagnosisInvulnerable;
                var trace = new GoldEvidenceTrace(gold, counter);
                _goldEvidence = trace;
                GoldFeelEvents.Landed += trace.Landed;
                GoldFeelEvents.MagnetStart += trace.Magnet;
                FeelEvents.GoldCollected += trace.Credited;
                GoldFeelEvents.FlightArrived += trace.Arrived;
                try
                {
                    spawn.SpawningEnabledDev = false;
                    spawn.ClearAllEnemies();
                    gold.ClearOrbs(false);
                    rig.Health.DiagnosisInvulnerable = true;
                    StartCoroutine(Record(folder, "gold-hud"));
                    trace.Mark("start");
                    yield return new WaitForSecondsRealtime(2f);
                    yield return Still(stills, "01-gold-before");
                    yield return Kills(rig, 2.2f, 3);
                    yield return new WaitForSecondsRealtime(3f);
                    trace.Mark("manual-settled");
                    yield return Still(stills, "02-gold-manual-settled");
                    yield return Kills(rig, 9f, 3);
                    yield return new WaitForSecondsRealtime(1.2f);
                    yield return Still(stills, "03-gold-waiting");
                    trace.Mark("sweep-request");
                    bool completed = spawn.DebugCompleteRound();
                    trace.Mark(completed ? "round-completed" : "round-completion-refused");
                    yield return WaitFor(() => gold.LiveOrbCount == 0, 5f);
                    trace.Mark("sweep-settled");
                    yield return Still(stills, "04-gold-sweep-settled");
                    while (Time.realtimeSinceStartup - trace.Started < 35f)
                    {
                        yield return null;
                    }
                }
                finally
                {
                    _recording = false;
                    GoldFeelEvents.Landed -= trace.Landed;
                    GoldFeelEvents.MagnetStart -= trace.Magnet;
                    FeelEvents.GoldCollected -= trace.Credited;
                    GoldFeelEvents.FlightArrived -= trace.Arrived;
                    _goldEvidence = null;
                    if (spawn != null) spawn.SpawningEnabledDev = spawning;
                    if (rig != null && rig.Health != null) rig.Health.DiagnosisInvulnerable = invulnerable;
                    trace.Write(folder);
                }

                // Let Record drain its own GPU/readback jobs before the outer driver quits the Player.
                yield return WaitFor(() => _windowSegments.Count > 0, 20f);
                File.WriteAllText(Path.Combine(folder, "windows.json"), "{\"mode\":\"windows-gold\",\"screen\":\"" + Screen.width + "x" + Screen.height +
                    "\",\"videos\":[" + string.Join(",", _windowSegments) + "]}");
            }

            private sealed class GoldEvidenceTrace
            {
                public readonly float Started = Time.realtimeSinceStartup;
                private readonly GoldService _gold;
                private readonly UiCounter _counter;
                private readonly StringBuilder _samples = new StringBuilder("index\tseconds\tunityFrame\trealtime\twallet\tinFlight\tcounterTarget\tvisibleText\ttargetValid\ttargetX\ttargetY\n");
                private readonly StringBuilder _events = new StringBuilder("unityFrame\trealtime\tevent\tamount\tkind\tflew\twallet\tinFlight\tcounterTarget\n");
                private int _manualFlights, _sweepFlights, _heldSamples;
                private bool _targetSeen;

                public GoldEvidenceTrace(GoldService gold, UiCounter counter) { _gold = gold; _counter = counter; }
                private static string F(float n) { return n.ToString("0.0000", CultureInfo.InvariantCulture); }
                public void Sample(int index, float seconds)
                {
                    bool valid = HudGoldTarget.TryGetScreenPoint(out Vector2 point);
                    _targetSeen |= valid;
                    int balance = _gold.Wallet.Balance, flying = HudGoldTarget.InFlight;
                    if (flying > 0 && _counter.Target == Mathf.Max(0, balance - flying)) _heldSamples++;
                    _samples.Append(index).Append('\t').Append(F(seconds)).Append('\t').Append(Time.frameCount).Append('\t').Append(F(Time.realtimeSinceStartup))
                        .Append('\t').Append(balance).Append('\t').Append(flying).Append('\t').Append(_counter.Target).Append('\t')
                        .Append(_counter.ValueText != null ? _counter.ValueText.text.Replace("\t", " ").Replace("\n", " ") : "missing")
                        .Append('\t').Append(valid ? 1 : 0).Append('\t').Append(F(point.x)).Append('\t').Append(F(point.y)).Append('\n');
                }
                private void Event(string name, int amount, string kind, bool flew)
                {
                    _events.Append(Time.frameCount).Append('\t').Append(F(Time.realtimeSinceStartup)).Append('\t').Append(name).Append('\t').Append(amount)
                        .Append('\t').Append(kind).Append('\t').Append(flew ? 1 : 0).Append('\t').Append(_gold.Wallet.Balance)
                        .Append('\t').Append(HudGoldTarget.InFlight).Append('\t').Append(_counter.Target).Append('\n');
                }
                public void Mark(string name) { Event(name, 0, "", false); }
                public void Landed(GoldLandedFeel e) { Event("landed", e.Amount, "", false); }
                public void Magnet(GoldMagnetFeel e) { Event("magnet", e.Amount, "", false); }
                public void Credited(FeelGold e) { Event("credited", e.Amount, "", false); }
                public void Arrived(GoldArrivedFeel e)
                {
                    if (e.Flew)
                    {
                        if (e.Kind == GoldCollectKind.Manual) _manualFlights++;
                        else if (e.Kind == GoldCollectKind.RoundClearAuto) _sweepFlights++;
                    }
                    Event("arrived", e.Amount, e.Kind.ToString(), e.Flew);
                }
                public void Write(string folder)
                {
                    File.WriteAllText(Path.Combine(folder, "gold-samples.tsv"), _samples.ToString());
                    File.WriteAllText(Path.Combine(folder, "gold-events.tsv"), _events.ToString());
                    bool settled = HudGoldTarget.InFlight == 0 && _counter.Target == _gold.Wallet.Balance;
                    File.WriteAllText(Path.Combine(folder, "gold-summary.json"), "{\"targetSeen\":" + (_targetSeen ? "true" : "false") +
                        ",\"manualFlights\":" + _manualFlights + ",\"sweepFlights\":" + _sweepFlights + ",\"heldCounterSamples\":" + _heldSamples +
                        ",\"settled\":" + (settled ? "true" : "false") + ",\"screen\":\"" + Screen.width + "x" + Screen.height + "\"}");
                    Debug.Log("[UiCapture] G-M3 evidence target=" + _targetSeen + " manual=" + _manualFlights + " sweep=" + _sweepFlights +
                        " heldSamples=" + _heldSamples + " settled=" + settled + "; inspect TSV/MP4, not a runtime PASS claim");
                }
            }

            private static IEnumerator FocusLastInPane()
            {
                GameObject scroll = GameObject.Find(MainMenuScreen.PaneLook.ScrollName);
                Selectable last = null;
                if (scroll != null)
                {
                    foreach (Selectable control in scroll.GetComponentsInChildren<Selectable>())
                    {
                        if (control.IsInteractable() && control.gameObject.activeInHierarchy
                            && (last == null || control.transform.position.y < last.transform.position.y))
                        {
                            last = control;
                        }
                    }
                }

                if (last != null)
                {
                    UiInput.Focus(last);
                }

                yield return null;
            }

            private IEnumerator Still(string folder, string name)
            {
                yield return Frame(folder, name);
                _windowStills.Add(name);
            }

            private IEnumerator Pane(MainMenuScreen menu, UiContext pane, string folder, string name)
            {
                // The player's path: the rail button takes focus and is pressed (its word shows the open pane).
                GameObject tab = GameObject.Find("Tab." + pane);
                Selectable control = tab != null ? tab.GetComponentInChildren<Selectable>() : null;
                if (control != null)
                {
                    UiInput.Focus(control);
                    Submit(control.gameObject);
                }

                if (menu.Pane != pane || !menu.PaneOpen)
                {
                    menu.ShowPane(pane);
                }

                yield return new WaitForSecondsRealtime(1.3f);
                yield return Still(folder, name);
            }

            /// <summary>Thread-pool side of a recorded frame: the readback's row flip, JPEG encode (thread-safe), write.</summary>
            private sealed class RecordJob
            {
                public byte[] Buffer;
                public int Width, Height, Index;
                public float Seconds;
                public bool Flip;
                public string Folder;
                public RecordState State;
            }

            private sealed class RecordState
            {
                public int InFlight;
                public readonly List<KeyValuePair<int, float>> Written = new List<KeyValuePair<int, float>>();
            }

            /// <summary>
            /// Real-time frames until <see cref="_recording"/> drops, on a 1/30 s slot grid: the backbuffer copied on the GPU
            /// (CaptureScreenshotIntoRenderTexture), read back asynchronously and encoded on the thread pool - td-capture's
            /// pipeline (WP-099), so the recording does not slow the UI it records. Each frame's time since the first.
            /// </summary>
            private IEnumerator Record(string folder, string segment)
            {
                const float Slot = 1f / 30f;
                const int MaxInFlight = 8;
                string frames = Path.Combine(folder, "video", segment, "frames");
                Directory.CreateDirectory(frames);
                _recording = true;
                var state = new RecordState();
                var target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
                target.Create();
                bool flip = SystemInfo.graphicsUVStartsAtTop;
                float t0 = -1f, next = 0f;
                int n = 0, busy = 0;
                while (_recording)
                {
                    yield return new WaitForEndOfFrame();
                    if (!_recording)
                    {
                        break;
                    }

                    float now = Time.realtimeSinceStartup;
                    if (t0 < 0f)
                    {
                        t0 = now;
                    }

                    float t = now - t0;
                    if (t + 0.002f < next)
                    {
                        continue;
                    }

                    if (System.Threading.Volatile.Read(ref state.InFlight) >= MaxInFlight)
                    {
                        busy++;
                        continue;
                    }

                    next = (Mathf.Floor(t / Slot) + 1f) * Slot;
                    int index = n++;
                    ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
                    if (_goldEvidence != null) _goldEvidence.Sample(index, t);
                    System.Threading.Interlocked.Increment(ref state.InFlight);
                    UnityEngine.Rendering.AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32, request =>
                    {
                        if (request.hasError)
                        {
                            System.Threading.Interlocked.Decrement(ref state.InFlight);
                            return;
                        }

                        var job = new RecordJob
                        {
                            Buffer = request.GetData<byte>().ToArray(), Width = request.width, Height = request.height,
                            Index = index, Seconds = t, Flip = flip, Folder = frames, State = state,
                        };
                        System.Threading.ThreadPool.QueueUserWorkItem(EncodeFrame, job);
                    });
                }

                float drain = Time.realtimeSinceStartup;
                while (System.Threading.Volatile.Read(ref state.InFlight) > 0 && Time.realtimeSinceStartup - drain < 15f)
                {
                    yield return null;
                }

                target.Release();
                Destroy(target);
                var times = new StringBuilder();
                lock (state.Written)
                {
                    state.Written.Sort((a, b) => a.Key.CompareTo(b.Key));
                    foreach (KeyValuePair<int, float> frame in state.Written)
                    {
                        times.Append(frame.Key.ToString(CultureInfo.InvariantCulture)).Append('\t')
                            .Append(frame.Value.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\n');
                    }
                }

                File.WriteAllText(Path.Combine(folder, "video", segment, "frames.tsv"), times.ToString());
                float seconds = t0 < 0f ? 0f : Time.realtimeSinceStartup - t0;
                int written = state.Written.Count;
                _windowSegments.Add("{\"name\":\"" + segment + "\",\"frames\":" + written.ToString(CultureInfo.InvariantCulture)
                    + ",\"seconds\":" + seconds.ToString("0.00", CultureInfo.InvariantCulture)
                    + ",\"fps\":" + (seconds > 0f ? written / seconds : 0f).ToString("0.0", CultureInfo.InvariantCulture)
                    + ",\"busySkipped\":" + busy.ToString(CultureInfo.InvariantCulture) + "}");
                Debug.Log("[UiCapture] windows video " + segment + ": " + written + " frames in " + seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s, busy " + busy);
            }

            private static readonly System.Threading.WaitCallback EncodeFrame = state =>
            {
                var job = (RecordJob)state;
                try
                {
                    if (job.Flip)
                    {
                        int row = job.Width * 4;
                        var swap = new byte[row];
                        for (int top = 0, bottom = job.Height - 1; top < bottom; top++, bottom--)
                        {
                            System.Buffer.BlockCopy(job.Buffer, top * row, swap, 0, row);
                            System.Buffer.BlockCopy(job.Buffer, bottom * row, job.Buffer, top * row, row);
                            System.Buffer.BlockCopy(swap, 0, job.Buffer, bottom * row, row);
                        }
                    }

                    byte[] jpg = ImageConversion.EncodeArrayToJPG(job.Buffer, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm,
                        (uint)job.Width, (uint)job.Height, 0, 88);
                    File.WriteAllBytes(Path.Combine(job.Folder, "f-" + job.Index.ToString("000000", CultureInfo.InvariantCulture) + ".jpg"), jpg);
                    lock (job.State.Written)
                    {
                        job.State.Written.Add(new KeyValuePair<int, float>(job.Index, job.Seconds));
                    }
                }
                catch (System.Exception)
                {
                    // a lost frame is a gap on the grid (tdmux repeats the previous one); the count in windows.json shows it
                }
                finally
                {
                    System.Threading.Interlocked.Decrement(ref job.State.InFlight);
                }
            };

            private static IEnumerator Kills(PlayerRig rig, float radius, int count)
            {
                if (rig == null)
                {
                    yield break;
                }

                int round = RunDirector.Instance != null ? RunDirector.Instance.Round : 1;
                for (int i = 0; i < count; i++)
                {
                    float angle = (i * 360f / count + 20f) * Mathf.Deg2Rad;
                    Vector3 at = rig.transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    ZombieEnemy zombie = EnemyFactory.Spawn(EnemyTier.Standard, round, at, Quaternion.identity);
                    yield return null;
                    if (zombie != null)
                    {
                        zombie.Kill();
                    }

                    yield return new WaitForSecondsRealtime(0.15f);
                }
            }

            private static Selectable[] Selectables(string prefix)
            {
                var found = new List<Selectable>();
                foreach (Selectable control in FindObjectsOfType<Selectable>())
                {
                    if (control.name.StartsWith(prefix, System.StringComparison.Ordinal) && control.IsInteractable() && control.gameObject.activeInHierarchy)
                    {
                        found.Add(control);
                    }
                }

                found.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
                return found.ToArray();
            }

            private static void Press(string name)
            {
                GameObject target = GameObject.Find(name);
                Selectable selectable = target != null ? target.GetComponentInChildren<Selectable>() : null;
                if (selectable == null)
                {
                    Debug.LogWarning("[UiCapture] windows: no control " + name);
                    return;
                }

                UiInput.Focus(selectable);
                Submit(selectable.gameObject);
            }

            private static void Submit(GameObject target)
            {
                EventSystem events = EventSystem.current;
                if (target != null && events != null)
                {
                    ExecuteEvents.Execute(target, new BaseEventData(events), ExecuteEvents.submitHandler);
                }
            }
        }
    }
}
#endif
