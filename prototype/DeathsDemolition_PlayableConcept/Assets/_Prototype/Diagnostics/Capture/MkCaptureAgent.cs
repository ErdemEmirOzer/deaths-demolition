#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using DD.Prototype.Audio;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Difficulty;
using DD.Prototype.Enemies;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-110 (S-01 mk-capture, C-00 11.10; S-11 §9 alignment) · event-triggered marketing capture of a bot-played run.
    /// DEVELOPMENT PLAYER / EDITOR ONLY (whole file under the M-55 guard); without <c>-ddMkCapture</c> it creates nothing.
    /// tools/mk-capture.sh launches it next to QaRunBot (<c>-ddQaBot</c>) inside the user-data guard and muxes the clips.
    ///
    /// MASTER: the TopDown camera renders into an off-screen <c>-ddMkSize</c> target (default 1080×1920 portrait; the window shows
    /// a scaled preview only) with the vertical field of view <c>-ddMkFov</c> (default 68.6° = the game's 42° as the master's
    /// horizontal angle) at the <c>-ddMkCamera</c> Y/O/G distance, leaning up to <c>-ddMkLookMetres</c> toward the nearby crowd
    /// (PrototypeCameraRig.CaptureFovDev / CaptureDistanceScaleDev / CaptureFocusOffsetDev). The other formats are centre crops
    /// (the script). The engine's "Development Build" watermark is drawn on the window, never into the master.
    ///
    /// HUD (<c>-ddMkHud off|on|both</c>): off = every canvas disabled, the game camera alone. on / both = the screen-space overlay
    /// canvases are moved to a private UI camera that renders them twice per grabbed frame (cleared black, cleared white) at the
    /// master size; <see cref="MkHudComposite"/> lays them over the game frame (difference matting, linear). both = the two
    /// streams side by side (frames/ and frames-hud/).
    ///
    /// FRAMES: every end of frame on the <c>-ddMkFps</c> grid the target(s) are read back asynchronously and JPEG-encoded on the
    /// thread pool into a preroll ring (<see cref="MkFrameRing"/>, ≤ <c>-ddMkRingMB</c>) — nothing reaches the disk until a clip
    /// starts. HIGHLIGHTS (<see cref="MkHighlightDirector"/>, weights <see cref="MkWeights"/>): FeelEvents kills / multi-kills
    /// (marketing beat from <c>-ddMkMultiKillMin</c> bodies) / explosions / low-health escape / last zombie, Special Weapon
    /// activations, rocket bursts (≥ 3 rl_1 shots in 1 s), Elite and Ultra bodies first within 18 m. A clip takes the ring's
    /// frames from trigger − preroll and every later frame to its planned end. Per clip under clips/NN/: frames[-hud]/f-NNNNNN.jpg,
    /// frames.tsv (index, session t), stills[-hud]/*.png (trigger + new heat peaks, ≤ <c>-ddMkStills</c>), clip.json (times,
    /// round, weapon, kills, crowd, camera, look offset, events). Session: events.tsv, audio-sync.tsv (td-capture's per-second
    /// WAV anchors), sync.json, summary.json. <c>-ddMkMusicOff 1</c> mutes the mixer's Music group (SFX + ambience + UI stay).
    ///
    /// Stops at <c>-ddMkSeconds</c>, after <c>-ddMkClips</c> clips, when the run ends or when the app quits under it; summary.json
    /// is written in every case. Same full-sandbox rule as td-capture.
    /// </summary>
    public sealed class MkCaptureAgent : MonoBehaviour
    {
        private const float SceneTimeoutSeconds = 240f;
        private const int MaxInFlight = 6;
        private const float DrainTimeoutSeconds = 15f;
        private const float FinalizeMarginSeconds = 1.5f;
        private const float HudRefreshSeconds = 0.25f;
        private const float NearMetres = 18f;
        private const float CrowdMetres = 20f;
        private const float PollSeconds = 0.5f;
        private const float StillDelaySeconds = 0.35f;
        private const float StillGapSeconds = 2f;
        private const float SpecialGapSeconds = 1.5f;
        private const float MinKeptClipSeconds = 5f;
        private const float CalibrationTimeoutSeconds = 5f;
        private const float LookSmoothSeconds = 1.2f;
        private const float MusicMutedDb = -80f;

        private static bool s_started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_started = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (s_started) return;
            TryStart(Environment.GetCommandLineArgs());
        }

        /// <summary>Starts the agent only when <paramref name="args"/> carries <c>-ddMkCapture &lt;dir&gt;</c>; returns whether it did anything.</summary>
        public static bool TryStart(string[] args)
        {
            if (!MkCaptureOptions.IsRequested(args)) return false;
            s_started = true;
            string error;
            MkCaptureOptions o = MkCaptureOptions.Parse(args, out error);
            if (o == null)
            {
                string dir = TdCaptureOptions.Arg(args, MkCaptureOptions.OutFlag);
                WriteErrorFile(Path.IsPathRooted(dir ?? "") ? dir : null, "args", error, 2);
                Quit(2);
                return true;
            }

            if (!Application.isEditor && !TdCaptureOptions.CheckSandbox(args, out error))
            {
                WriteErrorFile(o.OutDir, "sandbox", error, 2);
                Quit(2);
                return true;
            }

            var go = new GameObject("MkCaptureAgent");
            DontDestroyOnLoad(go);
            go.AddComponent<MkCaptureAgent>()._o = o;
            Debug.Log("[MkCapture] armed out=" + o.OutDir + " master=" + o.Width + "x" + o.Height + " fps=" + o.Fps + " fov=" + o.Fov + " camera=" + o.Camera +
                      " hud=" + Hud(o) + " clips=" + o.Clips + " seconds=" + o.Seconds + " threshold=" + o.Threshold + " preroll=" + o.Preroll + " musicOff=" + o.MusicOff);
            return true;
        }

        private static string Hud(MkCaptureOptions o) => o.HudOn ? (o.HudOff ? "both" : "on") : "off";

        private MkCaptureOptions _o;
        private bool _finished, _stopping, _runEnded, _sessionLive;
        private string _stopReason;
        private double _stopT = -1d;

        // ---------------------------------------------------------------- targets + pipeline

        /// <summary>One grabbed frame: up to three readbacks (game, UI over black, UI over white) joined before the encode.</summary>
        private sealed class FrameJob
        {
            public MkCaptureAgent Owner;
            public long Index;
            public double T;
            public string StillPath, StillHudPath;   // a still: PNGs instead of JPEG frames
            public byte[] Game, UiBlack, UiWhite;
            public int Width, Height, Pending;
            public bool Failed;
        }

        private struct Encoded
        {
            public long Index;
            public double T;
            public byte[] Jpeg, JpegHud;
        }

        private static readonly WaitCallback s_encode = Encode;
        private static readonly WaitCallback s_write = WriteFile;

        private Camera _cam, _uiCam;
        private RenderTexture _master, _previousTarget, _uiBlack, _uiWhite;
        private bool _flip;
        private string _flipMethod = "not-measured";
        private int _flipMeasured = -1;
        private double _flipErrStraight, _flipErrFlipped;
        private TdFrameSchedule _schedule;
        private long _nextIndex;
        private int _inFlight, _encoding, _writing;
        private int _encodeFailed, _writeFailed, _readbackErrors, _framesEncoded, _framesWritten, _stillsWritten, _uiRenders;
        private string _lastError;
        private readonly ConcurrentQueue<Encoded> _done = new ConcurrentQueue<Encoded>();
        private readonly Stack<byte[]> _pool = new Stack<byte[]>();
        private MkFrameRing _ring;
        private MkHighlightDirector _director;
        private MkRawBudget _raw;           // gt M1: clip frames + stills written this session
        private MkEventLog _log;            // gt S3: the recent events (a clip's preroll part, the burst's kind)
        private MkBurstBeat _beat;          // S-11 12.10: the multi-kill beat counted from the kills
        private readonly MkRocketBurst _rockets = new MkRocketBurst();

        // ---------------------------------------------------------------- clock + audio

        private double _t0Realtime, _t0Dsp;
        private StreamWriter _audioTsv, _eventsTsv;
        private double _nextAudioSync = -1d;
        private bool _audioAnchored;
        private double _audioAnchorT;
        private long _audioAnchorF;
        private int _audioRate;
        private string _audioPath;
        private AudioMixer _mixer;
        private bool _musicMuted;
        private string _treeJson = "null";   // WP-104: the run tree at the session start (-ddTreePreset), read once

        // ---------------------------------------------------------------- camera presets

        private Vector3 _lookTarget, _look, _lookVelocity;
        private Vector3 _frame, _frameTarget, _frameVelocity;   // dry-1: the hero band correction (MkFraming)
        private int _frameCorrections;
        private float _frameMax;
        private const float FrameSmoothSeconds = 0.35f;
        private int _crowdNear, _aliveTotal;

        // ---------------------------------------------------------------- clips

        private sealed class Clip
        {
            public int Number;
            public string Dir;
            public double Start, End, Trigger, FinalizeAt = -1d, LastStillT = double.NegativeInfinity, PendingStillT = -1d, PeakT;
            public float PeakHeat;
            public float PeakHeatAtLastStill;
            public bool Closing, Truncated;
            public int Stills, Kills, CrowdAtTrigger, AliveAtTrigger, RoundAtTrigger, MaxGroupKills, MaxChainKills;
            public string TriggerKind, WeaponAtTrigger;
            public Vector3 LookAtTrigger;
            public readonly List<KeyValuePair<long, double>> Frames = new List<KeyValuePair<long, double>>(1200);
            public readonly List<string> StillFiles = new List<string>(4);
            public readonly StringBuilder Events = new StringBuilder();
            public int EventCount;
        }

        private Clip _clip;
        private readonly List<string> _clipSummaries = new List<string>(16);
        private int _clipsKept, _clipsDropped;
        private int _eventCount;
        private readonly int[] _eventKinds = new int[MkWeights.KindCount];
        private MkEventKind _lastKind;

        // ---------------------------------------------------------------- start

        private IEnumerator Start()
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(_o.OutDir, "clips"));
            }
            catch (Exception e)
            {
                Fail("outdir", "cannot create " + _o.OutDir + ": " + e.Message, 2);
                yield break;
            }

            if (!Application.isEditor && (!ProfileStore.HasPathOverride || !UiDataPaths.IsProcessLocal))
            {
                Fail("sandbox", "refused: the profile / UI data are not redirected (ProfileStore.HasPathOverride=" + ProfileStore.HasPathOverride +
                                ", UiDataPaths.IsProcessLocal=" + UiDataPaths.IsProcessLocal + ")", 2);
                yield break;
            }

            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Fail("video", "AsyncGPUReadback is not supported on " + SystemInfo.graphicsDeviceType, 3);
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
            while (!RunPlayable())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Fail("scene-ready", "Run scene not playable within " + SceneTimeoutSeconds + " s; scene " + SceneManager.GetActiveScene().name, 3);
                    yield break;
                }

                yield return null;
            }

            if (CameraModeService.Current != CameraMode.TopDown)
            {
                Fail("scene-ready", "camera mode is " + CameraModeService.Current + "; mk-capture is TopDown only", 2);
                yield break;
            }

            _cam = PrototypeCameraRig.Active.Camera;
            if (_cam == null)
            {
                Fail("scene-ready", "the camera rig has no camera", 3);
                yield break;
            }

            _master = NewTarget("MkCapture.Master");
            _previousTarget = _cam.targetTexture;
            _cam.targetTexture = _master;
            if (_o.Fov > 0f) PrototypeCameraRig.CaptureFovDev = _o.Fov;
            PrototypeCameraRig.CaptureDistanceScaleDev = MkCaptureOptions.DistanceScale(_o.Camera);
            if (_o.HudOn) CreateUiCamera();
            if (_o.MusicOff) _mixer = Resources.Load<AudioMixer>(AudioMixerBridge.ResourcePath);
            RunEvents.RunEnded += OnRunEnded;
            Application.quitting += OnQuitting;
            Debug.Log("[MkCapture] master " + _o.Width + "x" + _o.Height + " on " + _cam.name + " fov " + (_o.Fov > 0f ? _o.Fov.ToString("F1", CultureInfo.InvariantCulture) : "game") +
                      " camera " + _o.Camera + " (distance x" + MkCaptureOptions.DistanceScale(_o.Camera).ToString("F2", CultureInfo.InvariantCulture) + ") hud " + Hud(_o));

            float warmEnd = Time.realtimeSinceStartup + _o.Warmup;
            while (Time.realtimeSinceStartup < warmEnd && !_runEnded)
            {
                MuteMusic();
                yield return null;
            }

            yield return Calibrate();
            if (_finished) yield break;

            int ringFrames = Mathf.CeilToInt((_o.Preroll + 1f) * _o.Fps);
            _ring = new MkFrameRing(ringFrames, _o.RingMb * 1024L * 1024L);
            _director = new MkHighlightDirector(_o.Threshold, _o.Preroll, _o.Postroll, _o.MinClip, _o.MaxClip, _o.Clips);
            _raw = new MkRawBudget(_o.RawGb);
            _log = new MkEventLog(_o.Preroll + _o.MaxClip + 5f);
            _beat = new MkBurstBeat(_o.MultiKillMin);
            _schedule = new TdFrameSchedule(_o.Fps);

            yield return null;
            yield return new WaitForEndOfFrame();
            BeginSession();

            while (true)
            {
                double t = Time.realtimeSinceStartupAsDouble - _t0Realtime;
                if (t >= _o.Seconds) { Stop("duration", t); break; }
                if (_runEnded) { Stop("run-ended", t); break; }
                if (_director.Exhausted && _clip == null) { Stop("clips-done", t); break; }
                if (_raw.Exceeded) { Stop("raw-budget", t); break; }   // gt M1: the disk is shared
                Step(t);
                yield return null;
                yield return new WaitForEndOfFrame();
            }

            yield return Drain();
            FinishSession();
            yield return null;
            Quit(0);
        }

        private RenderTexture NewTarget(string name)
        {
            var rt = new RenderTexture(_o.Width, _o.Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = name, filterMode = FilterMode.Bilinear };
            rt.Create();
            return rt;
        }

        private static bool RunPlayable()
        {
            Scene s = SceneManager.GetActiveScene();
            return s.name == PrototypeScenes.Run && s.isLoaded && RunDirector.Instance != null && RunDirector.Instance.Phase == RunPhase.Fighting &&
                   PlayerRegistry.Primary != null && PrototypeCameraRig.Active != null;
        }

        private void BeginSession()
        {
            _treeJson = TreeState();
            _t0Realtime = Time.realtimeSinceStartupAsDouble;
            _t0Dsp = AudioSettings.dspTime;
            _audioRate = AudioSettings.outputSampleRate;
            _audioTsv = new StreamWriter(Path.Combine(_o.OutDir, "audio-sync.tsv"), false, new UTF8Encoding(false)) { NewLine = "\n" };
            _audioTsv.WriteLine("# t_seconds\twav_frames_captured\tdsp_time\trealtime\tdropped_samples");
            _eventsTsv = new StreamWriter(Path.Combine(_o.OutDir, "events.tsv"), false, new UTF8Encoding(false)) { NewLine = "\n" };
            _eventsTsv.WriteLine("# t\tkind\tweight\theat\tclip\tdetail");
            FeelEvents.EnemyKilled += OnKill;
            FeelEvents.MultiKill += OnMultiKill;
            FeelEvents.Explosion += OnExplosion;
            FeelEvents.LowHealth += OnLowHealth;
            FeelEvents.LastZombie += OnLastZombie;
            FeelEvents.ShotFired += OnShot;
            SpecialWeaponUsageEvents.Usage += OnSpecial;
            _sessionLive = true;
            TryAnchorAudio(0d);
            WriteAudioSync(0d);
            _nextAudioSync = 1d;
            WriteSyncJson();
            Debug.Log("[MkCapture] session t0 realtime=" + _t0Realtime.ToString("F3", CultureInfo.InvariantCulture) + " flip=" + _flip + " (" + _flipMethod + ")");
        }

        // ---------------------------------------------------------------- per frame

        private float _nextPoll;

        private void Step(double t)
        {
            Route();
            MkDecision d = _director.Tick(t, _ring.Count > 0 ? _ring.OldestT : t);
            if (d == MkDecision.Start) OpenClip(t);
            else if (d == MkDecision.End && _clip != null) CloseClip(t, false);
            if (_clip != null && !_clip.Closing) _clip.End = _director.ClipEnd;   // events push the planned end (≤ start + maxClip)
            if (_clip != null && !_clip.Closing && _director.PeakHeat > _clip.PeakHeatAtLastStill * 1.25f && _clip.Stills < _o.Stills &&
                _clip.PendingStillT < 0d && t - _clip.LastStillT >= StillGapSeconds)
            {
                _clip.PendingStillT = t + StillDelaySeconds;   // a new peak: one more still once the effect has bloomed
                _clip.PeakHeatAtLastStill = _director.PeakHeat;
            }

            if (_clip != null && _clip.Closing && t >= _clip.FinalizeAt) FinalizeClip();   // gt S2: before the still check
            bool still = _clip != null && _clip.PendingStillT >= 0d && t >= _clip.PendingStillT;

            long slot;
            bool due = _schedule.TryTake(t, Volatile.Read(ref _inFlight) >= MaxInFlight, out slot);
            if (due || still) Grab(t, due, still);

            if (t >= _nextAudioSync)
            {
                if (!_audioAnchored) TryAnchorAudio(t);
                WriteAudioSync(t);
                _nextAudioSync = Math.Floor(t) + 1d;
                MuteMusic();
            }

            if (Time.unscaledTime >= _nextPoll)
            {
                _nextPoll = Time.unscaledTime + PollSeconds;
                Poll(t);
            }

            _look = Vector3.SmoothDamp(_look, _lookTarget, ref _lookVelocity, LookSmoothSeconds, Mathf.Infinity, Time.unscaledDeltaTime);
            if (_o.FrameBand) FrameHero();
            _frame = Vector3.SmoothDamp(_frame, _frameTarget, ref _frameVelocity, FrameSmoothSeconds, Mathf.Infinity, Time.unscaledDeltaTime);
            PrototypeCameraRig.CaptureFocusOffsetDev = _look + _frame;
        }

        /// <summary>Encoded frames back from the pool: into the open clip (written), else into the preroll ring.</summary>
        private void Route()
        {
            Encoded e;
            while (_done.TryDequeue(out e))
            {
                if (_clip != null && e.T >= _clip.Start && e.T <= _clip.End) WriteClipFrame(e.Index, e.T, e.Jpeg, e.JpegHud);
                else _ring.Push(e.Index, e.T, e.Jpeg, e.JpegHud);
            }
        }

        /// <summary>End of frame: the UI renders (HUD streams) and the readbacks of this frame; a still and/or a video frame.</summary>
        private void Grab(double t, bool frame, bool still)
        {
            if (_o.HudOn) RenderUi();
            if (frame) Request(new FrameJob { Owner = this, Index = _nextIndex++, T = t });
            if (still)
            {
                Clip c = _clip;
                c.PendingStillT = -1d;
                if (c.Stills < _o.Stills)
                {
                    c.Stills++;
                    c.LastStillT = t;
                    string file = "s" + c.Stills + "-t" + t.ToString("F2", CultureInfo.InvariantCulture) + ".png";
                    c.StillFiles.Add(file);
                    Request(new FrameJob
                    {
                        Owner = this, Index = -1, T = t,
                        StillPath = _o.HudOff ? Path.Combine(c.Dir, "stills", file) : null,
                        StillHudPath = _o.HudOn ? Path.Combine(c.Dir, "stills-hud", file) : null
                    });
                }
            }
        }

        private void Request(FrameJob job)
        {
            Interlocked.Increment(ref _inFlight);
            job.Pending = _o.HudOn ? 3 : 1;
            AsyncGPUReadback.Request(_master, 0, TextureFormat.RGBA32, req => OnReadback(req, job, 0));
            if (!_o.HudOn) return;
            AsyncGPUReadback.Request(_uiBlack, 0, TextureFormat.RGBA32, req => OnReadback(req, job, 1));
            AsyncGPUReadback.Request(_uiWhite, 0, TextureFormat.RGBA32, req => OnReadback(req, job, 2));
        }

        private void OnReadback(AsyncGPUReadbackRequest req, FrameJob job, int which)
        {
            if (req.hasError || _finished)
            {
                if (req.hasError) Interlocked.Increment(ref _readbackErrors);
                job.Failed = true;
            }
            else
            {
                var data = req.GetData<byte>();
                byte[] buffer = Rent(data.Length);
                data.CopyTo(buffer);
                job.Width = req.width;
                job.Height = req.height;
                if (which == 0) job.Game = buffer;
                else if (which == 1) job.UiBlack = buffer;
                else job.UiWhite = buffer;
            }

            if (--job.Pending > 0) return;   // readback callbacks run on the main thread, one at a time
            if (job.Failed)
            {
                ReturnAll(job);
                Interlocked.Decrement(ref _inFlight);
                return;
            }

            Interlocked.Increment(ref _encoding);
            ThreadPool.QueueUserWorkItem(s_encode, job);
        }

        /// <summary>Thread pool: row flip, the HUD composite, JPEG frames into the queue (or PNG stills written); buffers back to the pool.</summary>
        private static void Encode(object state)
        {
            var job = (FrameJob)state;
            MkCaptureAgent owner = job.Owner;
            MkCaptureOptions o = owner._o;
            try
            {
                var scratch = new byte[job.Width * 4];
                if (owner._flip)
                {
                    TdOrientation.FlipRows(job.Game, job.Width, job.Height, scratch);
                    if (job.UiBlack != null) TdOrientation.FlipRows(job.UiBlack, job.Width, job.Height, scratch);
                    if (job.UiWhite != null) TdOrientation.FlipRows(job.UiWhite, job.Width, job.Height, scratch);
                }

                byte[] plain = null, hud = null;
                bool still = job.StillPath != null || job.StillHudPath != null;
                if (o.HudOff && (!still || job.StillPath != null)) plain = EncodeImage(job.Game, job.Width, job.Height, still ? 0 : o.JpegQuality);
                if (o.HudOn)
                {
                    MkHudComposite.Composite(job.Game, job.UiBlack, job.UiWhite, job.UiBlack);   // into the black buffer (reused)
                    hud = EncodeImage(job.UiBlack, job.Width, job.Height, still ? 0 : o.JpegQuality);
                }

                if (still)
                {
                    if (plain != null) { File.WriteAllBytes(job.StillPath, plain); owner._raw.Add(plain.Length); }
                    if (hud != null) { File.WriteAllBytes(job.StillHudPath, hud); owner._raw.Add(hud.Length); }
                    Interlocked.Increment(ref owner._stillsWritten);
                }
                else
                {
                    owner._done.Enqueue(new Encoded { Index = job.Index, T = job.T, Jpeg = plain, JpegHud = hud });
                    Interlocked.Increment(ref owner._framesEncoded);
                }
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref owner._encodeFailed);
                owner._lastError = "encode " + job.Index + ": " + e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                owner.ReturnAll(job);
                Interlocked.Decrement(ref owner._encoding);
                Interlocked.Decrement(ref owner._inFlight);
            }
        }

        /// <summary>quality 0 = PNG (stills), otherwise JPEG at that quality.</summary>
        private static byte[] EncodeImage(byte[] rgba, int w, int h, int quality)
        {
            return quality <= 0
                ? ImageConversion.EncodeArrayToPNG(rgba, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h)
                : ImageConversion.EncodeArrayToJPG(rgba, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, quality);
        }

        private sealed class WriteJob
        {
            public MkCaptureAgent Owner;
            public string Path;
            public byte[] Bytes;
        }

        private static void WriteFile(object state)
        {
            var job = (WriteJob)state;
            try
            {
                File.WriteAllBytes(job.Path, job.Bytes);
                Interlocked.Increment(ref job.Owner._framesWritten);
                job.Owner._raw.Add(job.Bytes.Length);
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref job.Owner._writeFailed);
                job.Owner._lastError = "write " + System.IO.Path.GetFileName(job.Path) + ": " + e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                Interlocked.Decrement(ref job.Owner._writing);
            }
        }

        private void WriteClipFrame(long index, double t, byte[] jpeg, byte[] jpegHud)
        {
            _clip.Frames.Add(new KeyValuePair<long, double>(index, t));
            string name = "f-" + index.ToString("000000", CultureInfo.InvariantCulture) + ".jpg";
            if (jpeg != null) QueueWrite(Path.Combine(_clip.Dir, "frames", name), jpeg);
            if (jpegHud != null) QueueWrite(Path.Combine(_clip.Dir, "frames-hud", name), jpegHud);
        }

        private void QueueWrite(string path, byte[] bytes)
        {
            Interlocked.Increment(ref _writing);
            ThreadPool.QueueUserWorkItem(s_write, new WriteJob { Owner = this, Bytes = bytes, Path = path });
        }

        private byte[] Rent(int length)
        {
            lock (_pool)
            {
                while (_pool.Count > 0)
                {
                    byte[] b = _pool.Pop();
                    if (b.Length == length) return b;
                }
            }

            return new byte[length];
        }

        private void ReturnAll(FrameJob job)
        {
            lock (_pool)
            {
                foreach (byte[] b in new[] { job.Game, job.UiBlack, job.UiWhite })
                {
                    if (b != null && _pool.Count < MaxInFlight * 3) _pool.Push(b);
                }
            }

            job.Game = job.UiBlack = job.UiWhite = null;
        }

        // ---------------------------------------------------------------- HUD

        private Canvas[] _canvases;
        private float _hudRefreshAt;
        private readonly HashSet<int> _hudHidden = new HashSet<int>(), _hudMoved = new HashSet<int>();

        /// <summary>The private UI camera of the HUD streams: never enabled (rendered by hand twice per grab), far from the world,
        /// a short far plane, no post-processing, every layer (the moved canvases keep their own layers).</summary>
        private void CreateUiCamera()
        {
            var go = new GameObject("MkCapture.UiCamera");
            DontDestroyOnLoad(go);
            go.transform.SetPositionAndRotation(new Vector3(0f, 20000f, 0f), Quaternion.identity);
            _uiCam = go.AddComponent<Camera>();
            _uiCam.enabled = false;
            _uiCam.clearFlags = CameraClearFlags.SolidColor;
            _uiCam.cullingMask = ~0;
            _uiCam.nearClipPlane = 0.01f;
            _uiCam.farClipPlane = 5f;
            _uiCam.allowHDR = false;
            _uiCam.allowMSAA = false;
            if (_o.HudSafe) _uiCam.rect = MkFraming.HudSafe;   // S-11: the HUD laid out inside the Reels safe rectangle
            _uiBlack = NewTarget("MkCapture.UiBlack");
            _uiWhite = NewTarget("MkCapture.UiWhite");
        }

        private void RenderUi()
        {
            RenderUiInto(_uiBlack, new Color(0f, 0f, 0f, 0f));
            RenderUiInto(_uiWhite, new Color(1f, 1f, 1f, 0f));
            _uiRenders++;
        }

        /// <summary>The whole target cleared first (a safe-rect camera clears only its viewport; outside it must read "no UI"
        /// in both renders), then the UI camera's render.</summary>
        private void RenderUiInto(RenderTexture target, Color background)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, background);
            RenderTexture.active = previous;
            _uiCam.targetTexture = target;
            _uiCam.backgroundColor = background;
            _uiCam.Render();
        }

        private void LateUpdate()
        {
            if (_o == null || _master == null || UiCurtain.IsCovering || SceneManager.GetActiveScene().name != PrototypeScenes.Run) return;
            if (_canvases == null || Time.unscaledTime >= _hudRefreshAt)
            {
                _canvases = FindObjectsOfType<Canvas>(true);
                _hudRefreshAt = Time.unscaledTime + HudRefreshSeconds;
            }

            foreach (Canvas c in _canvases)
            {
                if (c == null) continue;
                if (!_o.HudOn)
                {
                    if (!c.enabled) continue;
                    c.enabled = false;   // HUD-less: every canvas off, nested ones too (Canvas.enabled only; no gameplay state, no saved setting)
                    _hudHidden.Add(c.GetInstanceID());
                }
                else if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    c.renderMode = RenderMode.ScreenSpaceCamera;   // the HUD streams: onto the private UI camera, at the master's size
                    c.worldCamera = _uiCam;
                    c.planeDistance = 1f;
                    _hudMoved.Add(c.GetInstanceID());
                }
            }
        }

        /// <summary>The window shows the master scaled to fit — a monitor only; the capture reads the off-screen targets.</summary>
        private void OnGUI()
        {
            if (_master == null || Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _master, ScaleMode.ScaleToFit, false);
        }

        private void MuteMusic()
        {
            if (!_o.MusicOff || _mixer == null) return;
            _musicMuted = _mixer.SetFloat(AudioMixerBridge.VolumeParams[1], MusicMutedDb);   // "MusicVolume"; re-asserted each second
        }

        // ---------------------------------------------------------------- clips

        private void OpenClip(double t)
        {
            if (_clip != null) FinalizeClip();   // gt S1: the previous clip was still closing (a long frame) — keep it, never overwrite
            int n = _director.Clips;
            MkEventKind? burst = _log.TriggerKind(t, MkHighlightDirector.Window);
            var c = new Clip
            {
                Number = n, Start = _director.ClipStart, End = _director.ClipEnd, Trigger = t, TriggerKind = (burst ?? _lastKind).ToString(),
                RoundAtTrigger = RunDirector.Instance != null ? RunDirector.Instance.Round : 0, WeaponAtTrigger = WeaponRegistry.SelectedWeapon.ToString(),
                CrowdAtTrigger = _crowdNear, AliveAtTrigger = _aliveTotal, LookAtTrigger = _look + _frame
            };
            c.Dir = Path.Combine(_o.OutDir, "clips", n.ToString("00", CultureInfo.InvariantCulture));
            if (_o.HudOff) { Directory.CreateDirectory(Path.Combine(c.Dir, "frames")); Directory.CreateDirectory(Path.Combine(c.Dir, "stills")); }
            if (_o.HudOn) { Directory.CreateDirectory(Path.Combine(c.Dir, "frames-hud")); Directory.CreateDirectory(Path.Combine(c.Dir, "stills-hud")); }
            _clip = c;
            foreach (MkEventLog.Entry e in _log.Since(c.Start)) AppendClipEvent(c, e.T, e.Kind, e.Weight, e.Detail, e.Count, e.Chain);   // gt S3: the preroll's events
            foreach (MkFrameRing.Frame f in _ring.TakeFrom(c.Start)) WriteClipFrame(f.Index, f.T, f.Jpeg, f.JpegHud);
            c.PendingStillT = t + StillDelaySeconds;
            c.PeakHeatAtLastStill = _director.PeakHeat;
            Debug.Log("[MkCapture] clip " + n + " start " + TdJson.Num(c.Start, 2) + " trigger " + TdJson.Num(t, 2) + " (" + c.TriggerKind + ") heat " +
                      TdJson.Num(_director.PeakHeat, 1) + " planned end " + TdJson.Num(c.End, 2));
        }

        /// <summary>The director ended the clip (or the session is stopping): late frames up to End are still taken for FinalizeMarginSeconds.</summary>
        private void CloseClip(double t, bool truncated)
        {
            _clip.End = truncated ? Math.Min(_director.ClipEnd, t) : _director.ClipEnd;
            _clip.PeakT = _director.PeakT;            // kept on the clip: a next clip may open (and reset the director) before
            _clip.PeakHeat = _director.PeakHeat;      // this one is finalized (gt S1)
            _clip.Closing = true;
            _clip.Truncated = truncated;
            _clip.FinalizeAt = t + FinalizeMarginSeconds;
        }

        private void FinalizeClip()
        {
            Clip c = _clip;
            _clip = null;
            c.Frames.Sort((a, b) => a.Key.CompareTo(b.Key));
            double first = c.Frames.Count > 0 ? c.Frames[0].Value : c.Start;
            double last = c.Frames.Count > 0 ? c.Frames[c.Frames.Count - 1].Value : c.Start;
            bool keep = c.Frames.Count > 0 && last - first >= MinKeptClipSeconds;
            try
            {
                using (var tsv = new StreamWriter(Path.Combine(c.Dir, "frames.tsv"), false, new UTF8Encoding(false)) { NewLine = "\n" })
                {
                    foreach (KeyValuePair<long, double> f in c.Frames) tsv.WriteLine(f.Key.ToString(CultureInfo.InvariantCulture) + "\t" + f.Value.ToString("F6", CultureInfo.InvariantCulture));
                }

                var sb = new StringBuilder();
                sb.Append("{\n  \"wp\": \"WP-110\", \"clip\": ").Append(c.Number).Append(", \"kept\": ").Append(TdJson.Bool(keep))
                  .Append(", \"truncated\": ").Append(TdJson.Bool(c.Truncated)).Append(",\n");
                sb.Append("  \"start\": ").Append(TdJson.Num(c.Start, 3)).Append(", \"end\": ").Append(TdJson.Num(c.End, 3))
                  .Append(", \"trigger\": ").Append(TdJson.Num(c.Trigger, 3)).Append(", \"triggerKind\": ").Append(TdJson.Str(c.TriggerKind))
                  .Append(", \"peak\": ").Append(TdJson.Num(c.PeakT, 3)).Append(", \"peakHeat\": ").Append(TdJson.Num(c.PeakHeat, 2)).Append(",\n");
                sb.Append("  \"round\": ").Append(c.RoundAtTrigger).Append(", \"weapon\": ").Append(TdJson.Str(c.WeaponAtTrigger))
                  .Append(", \"killsInClip\": ").Append(c.Kills).Append(", \"maxGroupKills\": ").Append(c.MaxGroupKills).Append(", \"maxChainKills\": ").Append(c.MaxChainKills).Append(", \"crowdWithin20mAtTrigger\": ").Append(c.CrowdAtTrigger)
                  .Append(", \"aliveAtTrigger\": ").Append(c.AliveAtTrigger).Append(",\n");
                sb.Append("  \"camera\": ").Append(TdJson.Str(_o.Camera.ToString())).Append(", \"fov\": ").Append(TdJson.Num(_o.Fov, 1))
                  .Append(", \"lookOffsetAtTrigger\": [").Append(TdJson.Num(c.LookAtTrigger.x, 2)).Append(", ").Append(TdJson.Num(c.LookAtTrigger.z, 2)).Append("]")
                  .Append(", \"hud\": ").Append(TdJson.Str(Hud(_o))).Append(",\n");
                sb.Append("  \"frames\": ").Append(c.Frames.Count).Append(", \"firstFrameT\": ").Append(TdJson.Num(first, 3)).Append(", \"lastFrameT\": ").Append(TdJson.Num(last, 3))
                  .Append(", \"effectiveFps\": ").Append(TdJson.Num(TdFrameSchedule.EffectiveFps(c.Frames.Count, last - first), 2)).Append(",\n");
                sb.Append("  \"stills\": [");
                for (int i = 0; i < c.StillFiles.Count; i++) sb.Append(i > 0 ? ", " : "").Append(TdJson.Str(c.StillFiles[i]));
                sb.Append("],\n  \"events\": [").Append(c.Events).Append("\n  ]\n}\n");
                File.WriteAllText(Path.Combine(c.Dir, "clip.json"), sb.ToString());
            }
            catch (Exception e)
            {
                _lastError = "clip " + c.Number + ": " + e.Message;
            }

            if (keep) _clipsKept++;
            else _clipsDropped++;
            _clipSummaries.Add("{\"clip\": " + c.Number + ", \"kept\": " + TdJson.Bool(keep) + ", \"triggerKind\": " + TdJson.Str(c.TriggerKind) + ", \"start\": " +
                               TdJson.Num(c.Start, 3) + ", \"end\": " + TdJson.Num(c.End, 3) + ", \"frames\": " + c.Frames.Count + ", \"kills\": " + c.Kills +
                               ", \"events\": " + c.EventCount + ", \"stills\": " + c.StillFiles.Count + ", \"truncated\": " + TdJson.Bool(c.Truncated) + "}");
            Debug.Log("[MkCapture] clip " + c.Number + " " + (keep ? "kept" : "dropped (too short)") + " frames " + c.Frames.Count + " " +
                      TdJson.Num(first, 2) + ".." + TdJson.Num(last, 2) + " s, kills " + c.Kills + ", stills " + c.StillFiles.Count);
        }

        // ---------------------------------------------------------------- highlight events

        private double Now => Time.realtimeSinceStartupAsDouble - _t0Realtime;
        private double _lastSpecialT = double.NegativeInfinity;
        private readonly HashSet<int> _nearSeen = new HashSet<int>();

        private void Note(MkEventKind kind, float weight, string detail, int count = 0, int chain = 0)
        {
            if (!_sessionLive || _stopping) return;
            double t = Now;
            _lastKind = kind;
            _director.Add(t, weight);
            _eventCount++;
            _eventKinds[(int)kind]++;
            float heat = _director.Heat(t);
            bool inClip = _clip != null && !_clip.Closing;
            _eventsTsv.WriteLine(t.ToString("F3", CultureInfo.InvariantCulture) + "\t" + kind + "\t" + weight.ToString("F1", CultureInfo.InvariantCulture) + "\t" +
                                 heat.ToString("F1", CultureInfo.InvariantCulture) + "\t" + (inClip ? _clip.Number : 0) + "\t" + detail);
            _log.Add(t, kind, weight, detail, count, chain);
            if (inClip) AppendClipEvent(_clip, t, kind, weight, detail, count, chain);
        }

        private static void AppendClipEvent(Clip c, double t, MkEventKind kind, float weight, string detail, int count, int chain)
        {
            if (kind == MkEventKind.Kill)
            {
                c.Kills++;
                c.MaxGroupKills = Math.Max(c.MaxGroupKills, count);   // S-11 12.10: the largest single pull / blast in the clip
                c.MaxChainKills = Math.Max(c.MaxChainKills, chain);
            }
            c.Events.Append(c.EventCount++ > 0 ? "," : "").Append("\n    {\"t\": ").Append(TdJson.Num(t, 3)).Append(", \"kind\": ").Append(TdJson.Str(kind.ToString()))
             .Append(", \"weight\": ").Append(TdJson.Num(weight, 1)).Append(", \"detail\": ").Append(TdJson.Str(detail)).Append("}");
        }

        private void OnKill(FeelKill k)
        {
            Note(MkEventKind.Kill, MkWeights.Of(MkEventKind.Kill, k.ChainKills, (int)k.Tier, _o.MultiKillMin),
                k.Tier + (k.FromSpecial ? " special " + k.SpecialId : " " + k.Classic) + " group " + k.GroupKills + " chain " + k.ChainKills, k.GroupKills, k.ChainKills);
            int burst = 0;
            float beat = _beat != null ? _beat.OnKill(k.GroupKills, k.ChainKills, out burst) : 0f;
            if (beat > 0f) Note(MkEventKind.MultiKill, beat, "burst " + burst + " (group " + k.GroupKills + ", chain " + k.ChainKills + ")", burst);
        }

        /// <summary>The game's tier event: its count is the bodies when the tier was reached (3 / 5 / 8), not the blast's total.</summary>
        private void OnMultiKill(FeelMultiKill m) => Note(MkEventKind.MultiKill, MkWeights.GameMultiKill(m.Count, _o.MultiKillMin),
            "game tier " + m.Tier + " at " + m.Count + (m.SameGroup ? " (pull)" : " (chain)"));
        private void OnExplosion(FeelExplosion e) => Note(MkEventKind.Explosion, MkWeights.Of(MkEventKind.Explosion, 0, 0), e.Source + " r " + TdJson.Num(e.Radius, 1));
        private void OnLastZombie(FeelLastZombie z) => Note(MkEventKind.LastZombie, MkWeights.Of(MkEventKind.LastZombie, 0, (int)z.Tier), "round " + z.Round);

        private void OnLowHealth(FeelLowHealth h)
        {
            if (h.Entered && !h.Dead) Note(MkEventKind.LowHealth, MkWeights.Of(MkEventKind.LowHealth, 0, 0), "ratio " + TdJson.Num(h.HealthRatio, 2));
        }

        private void OnShot(FeelShot s)
        {
            if (s.Special || s.Classic != ClassicWeaponId.Rl1 || !_sessionLive) return;
            if (_rockets.Shot(Now)) Note(MkEventKind.RocketBurst, MkWeights.Of(MkEventKind.RocketBurst, 0, 0), MkRocketBurst.Shots + " rockets in " + MkRocketBurst.Seconds + " s");
        }

        private void OnSpecial(SpecialUsageEvent u)
        {
            if (u.Kind != SpecialUsageKind.Activated && u.Kind != SpecialUsageKind.Fired) return;
            double t = Now;
            if (t - _lastSpecialT < SpecialGapSeconds) return;
            _lastSpecialT = t;
            Note(MkEventKind.Special, MkWeights.Of(MkEventKind.Special, 0, 0), u.Id + " " + u.Kind);
        }

        /// <summary>The hero outside <see cref="MkFraming"/>'s band of this frame: the needed camera shift on the hero's ground plane
        /// (moving the camera by hero − point-under-the-band-edge puts the hero on that edge), from the offset applied now.</summary>
        private void FrameHero()
        {
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig == null || _cam == null) return;
            Vector3 hero = rig.transform.position;
            Vector3 vp = _cam.WorldToViewportPoint(hero);
            if (vp.z <= 0f) return;
            float tx, ty;
            if (!MkFraming.BandTarget(vp.x, vp.y, out tx, out ty)) return;
            var plane = new Plane(Vector3.up, hero);
            Ray ray = _cam.ViewportPointToRay(new Vector3(tx, ty, 0f));
            float d;
            if (!plane.Raycast(ray, out d)) return;
            Vector3 delta = hero - ray.GetPoint(d);
            delta.y = 0f;
            _frameTarget = Vector3.ClampMagnitude(_frame + delta, MkFraming.MaxOffset);
            _frameCorrections++;
            _frameMax = Mathf.Max(_frameMax, _frameTarget.magnitude);
        }

        /// <summary>Every 0.5 s (dev only): Elite / Ultra bodies first within 18 m (the boss beat), the crowd within 20 m and its centre —
        /// the camera leans up to -ddMkLookMetres toward it (the look room on the threat's side).</summary>
        private void Poll(double t)
        {
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig == null) return;
            Vector3 p = rig.transform.position;
            Vector3 sum = Vector3.zero;
            int near = 0, alive = 0;
            foreach (ZombieEnemy z in FindObjectsOfType<ZombieEnemy>())
            {
                if (!z.IsAlive) continue;
                alive++;
                Vector3 d = z.transform.position - p;
                d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq <= CrowdMetres * CrowdMetres) { near++; sum += d; }
                if (z.Tier < EnemyTier.Elite || sq > NearMetres * NearMetres || !_nearSeen.Add(z.GetInstanceID())) continue;
                MkEventKind kind = z.Tier == EnemyTier.Ultra ? MkEventKind.UltraNear : MkEventKind.EliteNear;
                Note(kind, MkWeights.Of(kind, 0, (int)z.Tier), z.Tier + " at " + TdJson.Num(Mathf.Sqrt(sq), 1) + " m");
            }

            _crowdNear = near;
            _aliveTotal = alive;
            _lookTarget = near > 0 && _o.Look > 0f ? MkFraming.Look(sum / near, _o.Look) : Vector3.zero;
        }

        // ---------------------------------------------------------------- orientation

        /// <summary>The master read both ways in one end of frame — ReadPixels (texture row order) and the async readback the frames use;
        /// the luma band profiles decide the readback's row order (<see cref="TdOrientation"/>).</summary>
        private IEnumerator Calibrate()
        {
            yield return null;
            yield return new WaitForEndOfFrame();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _master;
            var reference = new Texture2D(_o.Width, _o.Height, TextureFormat.RGBA32, false);
            reference.ReadPixels(new Rect(0, 0, _o.Width, _o.Height), 0, 0, false);
            RenderTexture.active = previous;
            Color32[] px = reference.GetPixels32();
            var refBytes = new byte[px.Length * 4];
            for (int i = 0, o = 0; i < px.Length; i++) { refBytes[o++] = px[i].r; refBytes[o++] = px[i].g; refBytes[o++] = px[i].b; refBytes[o++] = px[i].a; }
            try
            {
                File.WriteAllBytes(Path.Combine(_o.OutDir, "ref-calibration.png"), reference.EncodeToPNG());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MkCapture] ref-calibration.png not written: " + e.Message);
            }

            Destroy(reference);
            byte[] grab = null;
            bool done = false;
            AsyncGPUReadback.Request(_master, 0, TextureFormat.RGBA32, req =>
            {
                done = true;
                if (req.hasError) return;
                var data = req.GetData<byte>();
                grab = new byte[data.Length];
                data.CopyTo(grab);
            });
            float deadline = Time.realtimeSinceStartup + CalibrationTimeoutSeconds;
            while (!done && Time.realtimeSinceStartup < deadline) yield return null;

            if (grab != null)
            {
                _flipMeasured = TdOrientation.ChooseFlip(TdOrientation.BandLuma(refBytes, _o.Width, _o.Height, TdOrientation.Bands),
                    TdOrientation.BandLuma(grab, _o.Width, _o.Height, TdOrientation.Bands), out _flipErrStraight, out _flipErrFlipped);
            }

            if (_flipMeasured >= 0) { _flip = _flipMeasured == 1; _flipMethod = "measured"; }
            else { _flip = false; _flipMethod = grab != null ? "ambiguous-fallback-straight" : "no-readback-fallback-straight"; }
            Debug.Log("[MkCapture] orientation flip=" + _flip + " (" + _flipMethod + ", err straight " + TdJson.Num(_flipErrStraight, 2) + " flipped " + TdJson.Num(_flipErrFlipped, 2) + ")");
        }

        // ---------------------------------------------------------------- audio anchors (td-capture's format)

        private void TryAnchorAudio(double t)
        {
            AudioOutputCapture audio = AudioOutputCapture.Armed;
            if (audio == null) return;
            _audioPath = audio.Path;
            long f = audio.FramesCaptured;
            if (f <= 0) return;
            _audioAnchored = true;
            _audioAnchorT = t;
            _audioAnchorF = f;
            WriteSyncJson();
        }

        private void WriteAudioSync(double t)
        {
            if (_audioTsv == null) return;
            AudioOutputCapture audio = AudioOutputCapture.Armed;
            _audioTsv.WriteLine(t.ToString("F6", CultureInfo.InvariantCulture) + "\t" + (audio != null ? audio.FramesCaptured.ToString(CultureInfo.InvariantCulture) : "") + "\t" +
                                AudioSettings.dspTime.ToString("F6", CultureInfo.InvariantCulture) + "\t" + Time.realtimeSinceStartupAsDouble.ToString("F6", CultureInfo.InvariantCulture) + "\t" +
                                (audio != null ? audio.DroppedSamples.ToString(CultureInfo.InvariantCulture) : ""));
            _audioTsv.Flush();
            _eventsTsv?.Flush();
        }

        private void WriteSyncJson()
        {
            double offset = _audioAnchored && _audioRate > 0 ? _audioAnchorF / (double)_audioRate - _audioAnchorT : double.NaN;
            var sb = new StringBuilder();
            sb.Append("{\n  \"wp\": \"WP-110\",\n  \"t0\": {\"realtimeSinceStartup\": ").Append(TdJson.Num(_t0Realtime, 6)).Append(", \"dspTime\": ").Append(TdJson.Num(_t0Dsp, 6)).Append("},\n");
            sb.Append("  \"frameTime\": \"clips/NN/frames.tsv t = realtimeSinceStartup at the end-of-frame grab - t0 (real seconds, session clock)\",\n");
            sb.Append("  \"audio\": {\"wav\": ").Append(TdJson.Str(_audioPath)).Append(", \"sampleRate\": ").Append(_audioRate)
              .Append(", \"anchorT\": ").Append(_audioAnchored ? TdJson.Num(_audioAnchorT, 6) : "null")
              .Append(", \"anchorFrames\": ").Append(_audioAnchored ? _audioAnchorF.ToString(CultureInfo.InvariantCulture) : "null")
              .Append(", \"wavOffsetSeconds\": ").Append(TdJson.Num(offset, 6)).Append(", \"musicOff\": ").Append(TdJson.Bool(_o.MusicOff)).Append("},\n");
            sb.Append("  \"orientation\": {\"flip\": ").Append(TdJson.Bool(_flip)).Append(", \"method\": ").Append(TdJson.Str(_flipMethod)).Append("},\n");
            sb.Append("  \"master\": [").Append(_o.Width).Append(", ").Append(_o.Height).Append("], \"fps\": ").Append(_o.Fps).Append(", \"hud\": ").Append(TdJson.Str(Hud(_o))).Append("\n}\n");
            try
            {
                File.WriteAllText(Path.Combine(_o.OutDir, "sync.json"), sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MkCapture] sync.json not written: " + e.Message);
            }
        }

        /// <summary>
        /// WP-104 (main 17f29c5d): the run tree the session plays — the -ddTreePreset asked, the preset the game recognised, whether
        /// the V2 tree is built and how many of the preset's nodes the run owns, so mk-capture can refuse a segment whose preset
        /// did not apply (an unknown name is ignored by the game).
        /// </summary>
        private static string TreeState()
        {
            string asked = null;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-ddTreePreset", StringComparison.Ordinal)) asked = args[i + 1];
            RoundUpgradeShop shop = RunDirector.Instance != null ? RunDirector.Instance.Shop : null;
            RunTreeV2 tree = shop != null ? shop.TreeV2 : null;
            bool built = tree != null && tree.IsBuilt;
            string preset = null;
            int presetNodes = 0, presetOwned = 0;
            if (TreePresetsV2.TryFromCommandLine(out TreePresetsV2.Preset p))
            {
                preset = p.Name;
                presetNodes = p.Nodes != null ? p.Nodes.Length : 0;
                if (built && p.Nodes != null)
                    for (int i = 0; i < p.Nodes.Length; i++)
                        if (tree.Owns(p.Nodes[i])) presetOwned++;
            }

            return "{\"asked\": " + TdJson.Str(asked) + ", \"preset\": " + TdJson.Str(preset) + ", \"v2\": " + TdJson.Bool(built) +
                   ", \"nodesOwned\": " + (built ? tree.NodesOwned : 0) + ", \"presetNodes\": " + presetNodes + ", \"presetNodesOwned\": " + presetOwned + "}";
        }

        // ---------------------------------------------------------------- stop

        private void OnRunEnded(RunSummary s)
        {
            _runEnded = true;
        }

        private void Stop(string reason, double t)
        {
            _stopping = true;
            _stopReason = reason;
            _stopT = t;
            if (_clip != null && !_clip.Closing)
            {
                _director.ForceEnd(t);
                CloseClip(t, true);
            }
        }

        /// <summary>Readbacks in flight and the late frames of a closing clip: up to DrainTimeoutSeconds, then the clip is finalized.</summary>
        private IEnumerator Drain()
        {
            float end = Time.realtimeSinceStartup + DrainTimeoutSeconds;
            while (Time.realtimeSinceStartup < end && (Volatile.Read(ref _inFlight) > 0 || (_clip != null && Now < _clip.FinalizeAt)))
            {
                Route();
                yield return null;
            }

            Route();
            if (_clip != null) FinalizeClip();
            while (Volatile.Read(ref _writing) > 0 && Time.realtimeSinceStartup < end + 5f) yield return null;
        }

        private void OnQuitting()
        {
            if (_finished || !_sessionLive) return;
            if (!_stopping) Stop("app-quitting", Now);
            DateTime end = DateTime.UtcNow.AddSeconds(3);
            while (Volatile.Read(ref _encoding) > 0 && DateTime.UtcNow < end) Thread.Sleep(10);
            Route();
            if (_clip != null) FinalizeClip();
            DateTime writeEnd = DateTime.UtcNow.AddSeconds(3);
            while (Volatile.Read(ref _writing) > 0 && DateTime.UtcNow < writeEnd) Thread.Sleep(10);
            FinishSession();
        }

        private void FinishSession()
        {
            if (_finished) return;
            _finished = true;
            _sessionLive = false;
            AudioOutputCapture audio = AudioOutputCapture.Armed;
            if (_audioTsv != null && _stopT >= 0d) WriteAudioSync(Now);   // dry-3 12.10: the row's t is when its frames / dsp / realtime are read, not the stop time
            try
            {
                if (_audioTsv != null) { _audioTsv.Dispose(); _audioTsv = null; }
                if (_eventsTsv != null) { _eventsTsv.Dispose(); _eventsTsv = null; }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MkCapture] closing the tsv files: " + e.Message);
            }

            var sb = new StringBuilder();
            sb.Append("{\n  \"wp\": \"WP-110\",\n  \"stopReason\": ").Append(TdJson.Str(_stopReason ?? "not-started")).Append(", \"durationSeconds\": ").Append(TdJson.Num(Math.Max(0d, _stopT), 3))
              .Append(", \"difficulty\": ").Append(TdJson.Str(DifficultyService.Id(DifficultyService.RunMode)))
              .Append(", \"tree\": ").Append(_treeJson).Append(",\n");   // WP-102: the mode the run played
            sb.Append("  \"master\": [").Append(_o.Width).Append(", ").Append(_o.Height).Append("], \"fps\": ").Append(_o.Fps).Append(", \"fov\": ").Append(TdJson.Num(_o.Fov, 1))
              .Append(", \"camera\": ").Append(TdJson.Str(_o.Camera.ToString())).Append(", \"lookMetres\": ").Append(TdJson.Num(_o.Look, 1))
              .Append(", \"hud\": ").Append(TdJson.Str(Hud(_o))).Append(", \"hudCanvasesHidden\": ").Append(_hudHidden.Count).Append(", \"hudCanvasesMoved\": ").Append(_hudMoved.Count)
              .Append(", \"uiRenders\": ").Append(_uiRenders).Append(", \"jpegQuality\": ").Append(_o.JpegQuality).Append(", \"musicOff\": ").Append(TdJson.Bool(_o.MusicOff))
              .Append(", \"musicMuted\": ").Append(TdJson.Bool(_musicMuted))
              .Append(", \"frameBand\": ").Append(TdJson.Bool(_o.FrameBand)).Append(", \"frameCorrections\": ").Append(_frameCorrections)
              .Append(", \"frameOffsetMax\": ").Append(TdJson.Num(_frameMax, 2)).Append(", \"hudSafe\": ").Append(TdJson.Bool(_o.HudSafe)).Append(",\n");
            sb.Append("  \"director\": {\"threshold\": ").Append(TdJson.Num(_o.Threshold, 1)).Append(", \"window\": ").Append(TdJson.Num(MkHighlightDirector.Window, 1))
              .Append(", \"preroll\": ").Append(TdJson.Num(_o.Preroll, 1)).Append(", \"postroll\": ").Append(TdJson.Num(_o.Postroll, 1))
              .Append(", \"minClip\": ").Append(TdJson.Num(_o.MinClip, 1)).Append(", \"maxClip\": ").Append(TdJson.Num(_o.MaxClip, 1)).Append(", \"maxClips\": ").Append(_o.Clips)
              .Append(", \"multiKillMin\": ").Append(_o.MultiKillMin).Append("},\n");
            sb.Append("  \"events\": {\"total\": ").Append(_eventCount);
            for (int i = 0; i < _eventKinds.Length; i++) sb.Append(", \"").Append(((MkEventKind)i).ToString()).Append("\": ").Append(_eventKinds[i]);
            sb.Append("},\n");
            sb.Append("  \"clipsKept\": ").Append(_clipsKept).Append(", \"clipsDropped\": ").Append(_clipsDropped).Append(",\n  \"clips\": [");
            for (int i = 0; i < _clipSummaries.Count; i++) sb.Append(i > 0 ? ",\n    " : "\n    ").Append(_clipSummaries[i]);
            sb.Append("\n  ],\n");
            sb.Append("  \"frames\": {\"grabbed\": ").Append(_nextIndex).Append(", \"encoded\": ").Append(Volatile.Read(ref _framesEncoded))
              .Append(", \"written\": ").Append(Volatile.Read(ref _framesWritten)).Append(", \"encodeFailed\": ").Append(Volatile.Read(ref _encodeFailed))
              .Append(", \"writeFailed\": ").Append(Volatile.Read(ref _writeFailed)).Append(", \"readbackErrors\": ").Append(Volatile.Read(ref _readbackErrors))
              .Append(", \"stillsWritten\": ").Append(Volatile.Read(ref _stillsWritten))
              .Append(", \"slotsTaken\": ").Append(_schedule != null ? _schedule.Taken : 0).Append(", \"slotsMissed\": ").Append(_schedule != null ? _schedule.MissedSlots : 0)
              .Append(", \"slotsBusy\": ").Append(_schedule != null ? _schedule.BusySkipped : 0)
              .Append(", \"sessionFps\": ").Append(TdJson.Num(TdFrameSchedule.EffectiveFps(_schedule != null ? _schedule.Taken : 0, _stopT), 2)).Append("},\n");
            sb.Append("  \"raw\": {\"budgetGB\": ").Append(TdJson.Num(_o.RawGb, 2)).Append(", \"writtenMB\": ")
              .Append(TdJson.Num(_raw != null ? _raw.Used / 1048576.0 : 0, 1)).Append("},\n");
            sb.Append("  \"ring\": {\"capacityFrames\": ").Append(_ring != null ? _ring.Capacity : 0).Append(", \"budgetMB\": ").Append(_o.RingMb)
              .Append(", \"peakMB\": ").Append(TdJson.Num(_ring != null ? _ring.PeakBytes / 1048576.0 : 0, 1)).Append(", \"budgetEvictions\": ").Append(_ring != null ? _ring.BudgetEvictions : 0).Append("},\n");
            sb.Append("  \"lastError\": ").Append(TdJson.Str(_lastError)).Append(",\n");
            sb.Append("  \"orientation\": {\"flip\": ").Append(TdJson.Bool(_flip)).Append(", \"method\": ").Append(TdJson.Str(_flipMethod)).Append(", \"measured\": ").Append(_flipMeasured)
              .Append(", \"errStraight\": ").Append(TdJson.Num(_flipErrStraight, 3)).Append(", \"errFlipped\": ").Append(TdJson.Num(_flipErrFlipped, 3)).Append("},\n");
            sb.Append("  \"audio\": {\"wav\": ").Append(TdJson.Str(_audioPath)).Append(", \"anchored\": ").Append(TdJson.Bool(_audioAnchored))
              .Append(", \"framesCapturedAtStop\": ").Append(audio != null ? audio.FramesCaptured.ToString(CultureInfo.InvariantCulture) : "null")
              .Append(", \"droppedSamples\": ").Append(audio != null ? audio.DroppedSamples.ToString(CultureInfo.InvariantCulture) : "null").Append("}\n}\n");
            try
            {
                File.WriteAllText(Path.Combine(_o.OutDir, "summary.json"), sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogError("[MkCapture] summary.json not written: " + e.Message);
            }

            Debug.Log("[MkCapture] done reason=" + _stopReason + " clips kept " + _clipsKept + " dropped " + _clipsDropped + " events " + _eventCount +
                      " frames grabbed " + _nextIndex + " written " + Volatile.Read(ref _framesWritten));
        }

        private void OnDestroy()
        {
            FeelEvents.EnemyKilled -= OnKill;
            FeelEvents.MultiKill -= OnMultiKill;
            FeelEvents.Explosion -= OnExplosion;
            FeelEvents.LowHealth -= OnLowHealth;
            FeelEvents.LastZombie -= OnLastZombie;
            FeelEvents.ShotFired -= OnShot;
            SpecialWeaponUsageEvents.Usage -= OnSpecial;
            RunEvents.RunEnded -= OnRunEnded;
            Application.quitting -= OnQuitting;
            PrototypeCameraRig.CaptureFovDev = 0f;
            PrototypeCameraRig.CaptureDistanceScaleDev = 1f;
            PrototypeCameraRig.CaptureFocusOffsetDev = Vector3.zero;
            _frame = _frameTarget = Vector3.zero;
            if (_cam != null && _cam.targetTexture == _master) _cam.targetTexture = _previousTarget;
            foreach (RenderTexture rt in new[] { _master, _uiBlack, _uiWhite })
            {
                if (rt != null) { rt.Release(); Destroy(rt); }
            }

            _master = _uiBlack = _uiWhite = null;
        }

        // ---------------------------------------------------------------- exits

        private void Fail(string stage, string message, int code)
        {
            if (_finished) return;
            _finished = true;
            WriteErrorFile(_o != null ? _o.OutDir : null, stage, message, code);
            Quit(code);
        }

        private static void WriteErrorFile(string outDir, string stage, string message, int code)
        {
            Debug.LogError("[MkCapture] " + stage + ": " + message + " (exit " + code + ")");
            if (string.IsNullOrEmpty(outDir)) return;
            try
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "error.json"),
                    "{\"wp\": \"WP-110\", \"stage\": " + TdJson.Str(stage) + ", \"error\": " + TdJson.Str(message) + ", \"exitCode\": " + code +
                    ", \"scene\": " + TdJson.Str(SceneManager.GetActiveScene().name) + "}\n");
            }
            catch (Exception e)
            {
                Debug.LogError("[MkCapture] error.json not written: " + e.Message);
            }
        }

        private static void Quit(int code)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }
    }
}
#endif
