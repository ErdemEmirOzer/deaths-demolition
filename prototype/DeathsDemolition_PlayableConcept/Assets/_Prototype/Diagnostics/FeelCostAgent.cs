#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Build;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Enemies;
using DD.Prototype.Damage;
using BodyRegion = DD.Prototype.Damage.BodyRegion;
using DD.Prototype.Feedback;
using DD.Prototype.Feedback.Vfx;
using DD.Prototype.Inputs;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace DD.Prototype.Diagnostics
{
    /// <summary>DEV opt-in synthetic cost replay, not combat acceptance, native rendering or C3 perf.
    /// Only run later through C00's guarded Player/slot/silent window. No file IO/logging during sampled frames.</summary>
    [DefaultExecutionOrder(-32700)]
    public sealed class FeelCostAgent : MonoBehaviour
    {
        private const int Frames = 3600, WarmFrames = 900, RoundFrames = 1800, CrowdWanted = 60, Seed = 95060;
        private enum PhaseKind { Steady, NaturalRound, Compatibility, DspOnly }
        [Serializable] private sealed class StateProof
        {
            public int round, phase, ledgerState, poolTotal, pool, alive, killed, spawned, completedRounds, totalKills, totalXp, roundXp, expectedRoundXp, goldIncome, wallet, reportRound, reportKills, reportGold;
            public bool frozen, roundHold;
        }
        [Serializable] private sealed class BranchProof
        { public long inactive, zeroGroup, mismatch, nonpositive, globalGuard, localAdjusted, offSkip; }
        [Serializable] private sealed class Window
        {
            public bool layersOn;
            public string phase, auxInputHash, deviceLayout, deviceInterface;
            public int plannedFrames, tracerAttempts, tracerSpawns, killInputs, damageInputs, naturalAsync, eligibleFrames, deviceId, deviceChanges, nativeDeviceFrames, compatibilityCalls;
            public bool preferenceStable = true, initialRumbleEnabled, initialProfileRumble, initialNativeDevice;
            public float initialIntensity, lastMotorIntensityReadback;
            public bool rumbleOwnerAvailable;
            public StateProof before, after; public BranchProof visualBranches;
            public RunDirector.FeelCostRoundObservation roundObservation;
            public int samples, roots, gcSamples, gcMaxB, crowdMin = int.MaxValue, crowdMax;
            public long gcTotalB, cpuTotalNs, cpuMaxNs, instrumentationNs;
            public double cpuMeanMs, actualSeconds, logicalSeconds = Frames / 60d;
            public int externalEvents, criticalEvents, fireStarts, fireStops, errors, routerOverflow;
            public int[] pathCalls, eventCalls, callbackCalls;
            public string inputHash, initialCrowdHash;
        }
        [Serializable] private sealed class Result
        {
            public string kind = "same-build-synthetic-steady-plus-natural-round-main-thread-feel-bundle";
            public string verdict = "UNMEASURED", cpuVerdict = "UNMEASURED", gcVerdict = "UNMEASURED";
            public string sourceSha, buildSha, buildStamp, error = "", scopeVersion = "wp095.cost.v2.c00-natural-round-compat-device";
            public int seed = Seed, crowd, crowdWanted = CrowdWanted, safetyCeiling, attempt, nominalHz = 60, framesPerWindow = Frames;
            public bool profilerEnabled;
            public string gcUnit;
            public string cpuMarker = FeelCostMeasurement.MarkerName, gcMarker = "GC.Alloc";
            public bool cpuRecorderValid, gcRecorderValid, cpuControlPass, gcControlPass, inputMatched, coverageComplete, durationMatched, provenanceMatched;
            public long cpuControlNs, cpuControlSamples, gcControlBytes;
            public int gcControlSamples, errorBits;
            public double deltaCpuMs;
            public string threadScope = "Synchronous outer main-thread scopes; GC bytes summed per rendered frame; not whole Player GC";
            public string cpuRule = "mean inclusive scoped ns/frame ON minus OFF <=0.3ms; nested scopes counted once; no overhead subtraction";
            public string exclusions = "Startup/lifecycle/reset/IO, unrelated base camera, worker/audio/render threads, GPU/native work outside listed calls";
            public string expandedScope = "Events includes synchronous subscribers; Router/LegacyHitStop/DeathSequence include existing shared presentation; not pure historical patch delta";
            public string roundRule = "Separate 1800-frame/30-logical-second natural round OFF/ON pair; each actual30+/-0.5s, pair drift<=0.3s; its mean/delta/GC never mixed with steady60";
            public string durationRule = "3600 identical ordered input steps at requested60fps =60 logical seconds; actual windows must each be within0.5s of60s and within0.3s of each other";
            public string instrumentation = "GC recorder Reset/Start/Stop/read outside CPU marker; their stopwatch ns reported separately; nesting counters inside inclusive scope remain measured";
            public string[] paths, callbackNames, missingCallbacks, callbackClasses;
            public bool[] original95, steadyRequired, roundRequired;
            public int originalRequiredCount;
            public string roundVerdict = "UNMEASURED", roundCpuVerdict = "UNMEASURED", roundGcVerdict = "UNMEASURED", productionRumbleVerdict = "UNMEASURED", compatibilityVerdict = "UNMEASURED", dspVerdict = "NOT_REQUESTED";
            public string backendHaptics = "OPEN: native device identity and mixer calls do not prove motor delivery or human sensation";
            public double roundDeltaCpuMs;
            public bool roundInputMatched, roundStateMatched, productionDeviceMatched;
            public Window roundOff, roundOn, compatibilityOff, compatibilityOn, dspOnly;
            public string callbackRule = "Original95 preserved. SteadyProduction required in steady ON; RoundEndProduction required in separate natural round ON; CompatibilityControl never substitutes production coverage. DSP_ONLY never substitutes production device calls. Warm-observed extras remain required; every branch/device variant is not implied";
            public bool[] expectedCallbacks;
            public int[] eventsExpected;
            public Window off, on;
            public bool gameplayPass = false, c3Pass = false, wholePlayerGcPass = false;
        }
        private struct Row
        {
            public int window, step, frame, roots, gcSamples, crowd, errors, phase, pool, killed, tracerAttempts, tracerSpawns, deviceId, eligible;
            public long visualInactive, visualZero, visualMismatch, visualGlobal, visualLocal, visualOff, visualNonpositive;
            public long cpuNs, cpuSamples, gcBytes, stopwatchNs, overheadNs;
        }
        private readonly Result _result = new Result();
        private Row[] _rows;
        private int[] _pathRows;
        private int _rowCount, _pending = -1, _step, _windowIndex, _plannedFrames, _naturalFirstLife;
        private PhaseKind _phaseKind;
        private uint _auxHash;
        private TracerEffect[] _tracers;
        private GamepadRumble _rumble;
        private TopDownRumbleMixer _dspMixer;
        private bool _dspRequested;
        private bool _collecting, _done, _stopNext, _saved;
        private string _out;
        private Window _window;
        private PlayerRig _player;
        private Transform _target;
        private int _crowd;
        private Vector3 _origin;
        private double _start;
        private int _oldCap, _oldVsync;
        private bool _oldProfiler;
        private float _oldScale, _oldFixed;
        private byte[] _bait;

        private static string Arg(string[] args, string flag)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == flag) return args[i + 1];
            return null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string[] args = Environment.GetCommandLineArgs(); string path = Arg(args, "-ddFeelCost");
            if (string.IsNullOrEmpty(path)) return;
            int attempt;
            if (!Path.IsPathRooted(path) || !int.TryParse(Arg(args, "-ddFeelCostAttempt"), out attempt) || attempt < 1 || attempt > 2)
            { Debug.LogError("feel-cost: absolute output and attempt1/2 required"); return; }
            // Conflicting diagnostic drivers destroy the matched input contract.
            if (Arg(args, "-ddTdCapture") != null || Arg(args, "-ddPilotPerfProbe") != null || Arg(args, "-ddQaBot") != null)
            { Debug.LogError("feel-cost: cannot combine with capture/pilot/bot"); return; }
            if (File.Exists(Path.Combine(path, "feel-cost.json")) || File.Exists(Path.Combine(path, "feel-cost.csv")))
            { Debug.LogError("feel-cost: refusing to overwrite evidence"); return; }
            var go = new GameObject("WP095_FeelCost"); DontDestroyOnLoad(go);
            var agent = go.AddComponent<FeelCostAgent>(); agent._out = path; agent._result.attempt = attempt;
            agent._result.sourceSha = Arg(args, "-ddFeelCostSha") ?? "MISSING";
            agent._dspRequested = Arg(args, "-ddFeelCostSampleOnly") == "1";
            go.AddComponent<FeelCostTail>().Owner = agent;
        }
        private IEnumerator Start()
        {
            _oldCap = Application.targetFrameRate; _oldVsync = QualitySettings.vSyncCount;
            _oldScale = Time.timeScale; _oldFixed = Time.fixedDeltaTime;
            Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            // R3 FINAL fix 37581477 run (10.10): in a development Player the built-in GC.Alloc marker yields samples only while the
            // Profiler is on (the 64 KiB control read 0 - "recorder positive control failed"); PilotPerfProbe switches it on the same
            // way for its GPU recorders. On for the whole run - the OFF and ON windows alike, so the ON − OFF delta stays fair.
            _oldProfiler = UnityEngine.Profiling.Profiler.enabled; UnityEngine.Profiling.Profiler.enabled = true;
            _result.profilerEnabled = UnityEngine.Profiling.Profiler.enabled;
            int rawCapacity = Frames * 2 + RoundFrames * 2 + 12;
            _rows = new Row[rawCapacity]; _pathRows = new int[rawCapacity * (int)FeelCostPath.Count];
            _result.paths = Enum.GetNames(typeof(FeelCostPath));
            _result.eventsExpected = new int[] {600,120,40,10,20,10,20,10,10,10,10,1,1,10,10};
            // Require the embedded clean DEV build stamp to match the runner's full source SHA.
            BuildInfo stamp = BuildInfo.Read();
            _result.buildStamp = stamp.Describe(); _result.buildSha = stamp.commit;
            _result.provenanceMatched = IsFullSha(_result.sourceSha) && stamp.stamped &&
                stamp.mode == "dev" && stamp.describe.IndexOf("dirty", StringComparison.OrdinalIgnoreCase) < 0 &&
                !string.IsNullOrEmpty(stamp.commit) && stamp.commit.Length >= 12 &&
                _result.sourceSha.StartsWith(stamp.commit, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(_out);
            Application.logMessageReceived += OnLog;
            FeelCostMeasurement.Arm();
            _result.cpuRecorderValid = FeelCostMeasurement.CpuValid; _result.gcRecorderValid = FeelCostMeasurement.GcValid;
            if (!_result.cpuRecorderValid || !_result.gcRecorderValid) { Fail("recorder unavailable"); yield break; }
            yield return PrepareScene(); if (_result.error.Length != 0) { Save(); yield break; }
            // Register this caller before sampling: table growth is forbidden inside a measured scope.
            using (FeelCostMeasurement.Enter(FeelCostPath.Control)) { }
            // Positive controls use EXACTLY the measurement Scope/CPU-marker/GC-recorder API and options.
            FeelCostMeasurement.BeginFrame(); FeelCostMeasurement.Sampling = true;
            using (FeelCostMeasurement.Enter(FeelCostPath.Control))
            {
                long until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 500; //2ms
                while (Stopwatch.GetTimestamp() < until) { }
            }
            FeelCostMeasurement.Sampling = false;
            yield return null;
            _result.cpuControlNs = FeelCostMeasurement.PreviousCpuNs(out _result.cpuControlSamples);
            _result.cpuControlPass = _result.cpuControlNs >= 1000000 && _result.cpuControlSamples == 1;
            FeelCostMeasurement.BeginFrame(); FeelCostMeasurement.Sampling = true;
            using (FeelCostMeasurement.Enter(FeelCostPath.Control)) { _bait = new byte[65536]; _bait[0] = 1; GC.KeepAlive(_bait); }
            FeelCostMeasurement.Sampling = false;
            _result.gcControlBytes = FeelCostMeasurement.FrameGcBytes; _result.gcControlSamples = FeelCostMeasurement.FrameGcSamples;
            // 9bdff9c6 run (10.10): with the Profiler on the 64 KiB control gave 1 sample of value 834 - the GC.Alloc MARKER sample
            // carries its time (ns) in a development Player, not the bytes. The control asks for the allocation event (and 64 KiB
            // only where the unit is bytes); acceptance stays "no allocation in a feel scope": any event fails (gcSamples, gcTotalB).
            _result.gcUnit = FeelCostMeasurement.GcUnit.ToString();
            _result.gcControlPass = _result.gcControlSamples > 0 && (FeelCostMeasurement.GcUnit == Unity.Profiling.ProfilerMarkerDataUnit.Bytes
                ? _result.gcControlBytes >= 65536 : _result.gcControlBytes > 0);
            _bait = null; yield return null;
            if (!_result.cpuControlPass || !_result.gcControlPass || FeelCostMeasurement.ErrorBits != 0)
            { Fail("recorder positive control failed; no acceptance samples"); yield break; }
            for (int side = 0; side < 2; side++)
            {
                yield return PrepareScene(); if (_result.error.Length != 0) { Save(); yield break; }
                _phaseKind = PhaseKind.Steady; _windowIndex = side;
                FeelCostMeasurement.LayersOn = side == 1; FeelCostMeasurement.GateActive = true;
                FeelCostMeasurement.RegistrationPhase = true;
                for (int i = 0; i < WarmFrames; i++) { Replay(i); yield return null; }
                Replay(Frames - 1);
                FeelCostMeasurement.RegistrationPhase = false;
                ResetPresentation();
                yield return MeasureWindow(PhaseKind.Steady, side, Frames);
                if (side == 0) _result.off = _window; else _result.on = _window;
            }
            // Natural ledger completion is a distinct pair, never mixed into the 60-crowd mean.
            for (int side = 0; side < 2; side++)
            {
                yield return PrepareScene(false); if (_result.error.Length != 0) { Save(); yield break; }
                _naturalFirstLife = int.MaxValue;
                for (int i = 0; i < ZombieEnemy.Live.Count; i++)
                    if (ZombieEnemy.Live[i] != null && ZombieEnemy.Live[i].IsAlive) _naturalFirstLife = Math.Min(_naturalFirstLife, ZombieEnemy.Live[i].LifeId);
                // No death/round event during warmup. Production observers use known IDs.
                yield return null;
                RunDirector.Instance.ResetFeelCostRoundObservations();
                yield return MeasureWindow(PhaseKind.NaturalRound, side, RoundFrames);
                if (side == 0) _result.roundOff = _window; else _result.roundOn = _window;
            }
            // Four explicit compatibility calls per side; this label cannot close active production coverage.
            for (int side = 0; side < 2; side++)
            {
                yield return PrepareScene(); if (_result.error.Length != 0) { Save(); yield break; }
                yield return MeasureWindow(PhaseKind.Compatibility, side, 4);
                if (side == 0) _result.compatibilityOff = _window; else _result.compatibilityOn = _window;
            }
            if (_dspRequested)
            {
                _dspMixer = new TopDownRumbleMixer(); _dspMixer.Event(.1f, .05f, .2f, Time.unscaledTime);
                _dspMixer.Sample(Time.unscaledTime, .5f, .3f, out _, out _); //warm; no device or preference writes.
                yield return MeasureWindow(PhaseKind.DspOnly, 1, 4); _result.dspOnly = _window;
            }
            Judge(); Save();
        }
        private IEnumerator PrepareScene(bool steady = true)
        {
            FeelCostMeasurement.Sampling = FeelCostMeasurement.GateActive = false; FeelCostMeasurement.LayersOn = true;
            ResetPresentation(); UnityEngine.Random.InitState(Seed);
            PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
            int previousScene = SceneManager.GetActiveScene().handle;
            PrototypeUiFlow.ReleaseSimulation(); PrototypeUiFlow.StartRun();
            double deadline = Time.realtimeSinceStartupAsDouble + 120d;
            while ((SceneManager.GetActiveScene().handle == previousScene || SceneManager.GetActiveScene().name != PrototypeScenes.Run || PlayerRegistry.Primary == null ||
                EnemySpawnDirector.Instance == null || RunDirector.Instance == null || RunDirector.Instance.Phase != RunPhase.Fighting) &&
                Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (Time.realtimeSinceStartupAsDouble >= deadline) { _result.error = "scene ready timeout"; yield break; }
            CameraModeService.Request(CameraMode.TopDown, true);
            if (CameraModeService.Current != CameraMode.TopDown) { _result.error = "TD required"; yield break; }
            _player = PlayerRegistry.Primary; _player.Health.DiagnosisInvulnerable = true;
            _origin = _player.transform.position;
            // R3 FINAL c4093280: "60 crowd unavailable" twice - the game never holds more than RoundConfigSection.
            // AbsoluteSafetyCeiling (40) alive, so the 60 of HIS-SOZLESMESI S6 could not be reached. The crowd is the game's own
            // maximum, recorded beside the 60 asked for.
            _result.safetyCeiling = EnemySpawnDirector.Instance.SafetyCeiling;
            _crowd = Math.Min(CrowdWanted, _result.safetyCeiling); _result.crowd = _crowd;
            EnemySpawnDirector.Instance.ForceActiveTarget(_crowd, steady);
            int targetCount = steady ? _crowd : Math.Min(_crowd, EnemySpawnDirector.Instance.RoundPoolTotal);
            deadline = Time.realtimeSinceStartupAsDouble + 90d;
            while (EnemySpawnDirector.Instance.ActiveCount != targetCount && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (EnemySpawnDirector.Instance.ActiveCount != targetCount) { _result.error = "crowd " + targetCount + " unavailable"; yield break; }
            _target = null;
            foreach (var z in ZombieEnemy.Live) if (z != null && z.IsAlive) { _target = z.transform; break; }
            MuzzleSystem.Ensure(); CameraFeelDriver.Ensure();
            yield return null;
            _ = HitFeedbackDirector.Instance; //create/prewarm before caching, never during a sampled spawn.
            _tracers = UnityEngine.Object.FindObjectsOfType<TracerEffect>(true); //prewarmed pool inventory, unmeasured.
            var installer = PrototypeInputInstaller.Instance;
            var field = typeof(PrototypeInputInstaller).GetField("_rumble", BindingFlags.Instance | BindingFlags.NonPublic);
            _rumble = installer != null && field != null ? field.GetValue(installer) as GamepadRumble : null;
            // Async natural callbacks have static IDs; no gameplay event is invoked to register them.

        }
        private void Update()
        {
            if (!_collecting) return;
            if (_stopNext) { FeelCostMeasurement.Sampling = false; return; }
            FeelCostMeasurement.BeginFrame(); FeelCostMeasurement.Sampling = true;
            ObserveDeviceFrame();
            FeelCostMeasurement.Replaying = true;
            try
            {
                if (_phaseKind == PhaseKind.Steady) Replay(_step);
                else if (_phaseKind == PhaseKind.NaturalRound) DriveNaturalRound(_step);
                else if (_phaseKind == PhaseKind.Compatibility)
                { MixAux(_step); _window.compatibilityCalls++; HitStopService.Request(.02f, 0f); }
                else
                {
                    MixAux(_step); FeelCostMeasurement.DspSampleCall = true;
                    try { _dspMixer.Sample(Time.unscaledTime, .5f, .3f, out _, out _); }
                    finally { FeelCostMeasurement.DspSampleCall = false; }
                }
            }
            finally { FeelCostMeasurement.Replaying = false; }
            if (++_step == _plannedFrames) _stopNext = true;
        }
        private void Replay(int step)
        {
            FeelCostMeasurement.Replaying = true;
            try
            {
                FeelEvents.ReplayCostStep(step, _target, _origin);
                if (step % 6 == 0) MuzzleSystem.Instance.Play(_player.transform, VfxWeaponProfiles.Ar1, false);
                if (step % 30 == 0) FeelEventRouter.Enqueue(ImpactFeelId.CriticalHit, step + 10000, _origin, Vector3.forward);
                if (step % 900 == 400) { RunFlowClock.AddHold("WP095.Cost"); RunFlowClock.RemoveHold("WP095.Cost"); }
                if (step % 900 == 600) DeathSequence.Begin(.5f, .02f, .05f, .02f);
                if (step % 360 == 160) SpawnCostTracer(step);
            }
            finally { FeelCostMeasurement.Replaying = false; }
        }
        private IEnumerator MeasureWindow(PhaseKind kind, int side, int frames)
        {
            ResetPresentation();
            _phaseKind = kind; _plannedFrames = frames; _windowIndex = kind == PhaseKind.Steady ? side : kind == PhaseKind.NaturalRound ? side + 2 : kind == PhaseKind.Compatibility ? side + 4 : 6;
            _window = new Window { phase = kind.ToString(), layersOn = side == 1, plannedFrames = frames, logicalSeconds = frames / 60d, initialCrowdHash = CrowdHash(), before = State() };
            var prefs = InputPreferencesStore.Current;   // the pad through Input/FeelCostDevices (only Input/ reads the Input System)
            FeelCostDevices.Read(out _window.deviceId, out _window.deviceLayout, out _window.deviceInterface, out _window.initialNativeDevice);
            _window.initialRumbleEnabled = prefs.RumbleEnabled;
            _window.initialProfileRumble = FeelProfileTD.Active.rumble; _window.initialIntensity = prefs.RumbleIntensity; _window.rumbleOwnerAvailable = _rumble != null;
            FeelCostMeasurement.LayersOn = side == 1; FeelCostMeasurement.GateActive = true;
            FeelCostMeasurement.NaturalRoundPhase = kind == PhaseKind.NaturalRound;
            FeelCostMeasurement.DiagnosticMixerOnly = kind == PhaseKind.DspOnly;
            FeelCostMeasurement.ClearWindow(); HitStopService.ResetFeelCostBranches();
            _auxHash = 2166136261u; _step = 0; _pending = -1; _done = _stopNext = false;
            _start = Time.realtimeSinceStartupAsDouble; _collecting = true;
            while (!_done) yield return null;
            FeelCostMeasurement.Sampling = false; FeelCostMeasurement.GateActive = false; FeelCostMeasurement.NaturalRoundPhase = false; FeelCostMeasurement.DiagnosticMixerOnly = false;
            _window.actualSeconds = Time.realtimeSinceStartupAsDouble - _start;
            _window.pathCalls = (int[])FeelCostMeasurement.PathCalls.Clone(); _window.callbackCalls = (int[])FeelCostMeasurement.MemberCalls.Clone();
            _window.eventCalls = (int[])FeelCostMeasurement.EventCalls.Clone(); _window.inputHash = FeelCostMeasurement.InputHash.ToString("x8", CultureInfo.InvariantCulture);
            _window.auxInputHash = _auxHash.ToString("x8", CultureInfo.InvariantCulture);
            _window.externalEvents = FeelCostMeasurement.ExternalEvents; _window.criticalEvents = FeelCostMeasurement.CriticalEvents;
            _window.fireStarts = FeelCostMeasurement.FireStarts; _window.fireStops = FeelCostMeasurement.FireStops; _window.naturalAsync = FeelCostMeasurement.NaturalAsyncEvents;
            _window.errors |= FeelCostMeasurement.ErrorBits; _window.routerOverflow = FeelEventRouter.QueueOverflow;
            _window.lastMotorIntensityReadback = _rumble != null ? _rumble.LastIntensity : 0f;
            _window.after = State(); _window.roundObservation = RunDirector.Instance.FeelCostRoundObservations;
            _window.visualBranches = new BranchProof { inactive = HitStopService.FeelCostVisualInactive, zeroGroup = HitStopService.FeelCostVisualGroupZero,
                mismatch = HitStopService.FeelCostVisualMismatch, nonpositive = HitStopService.FeelCostVisualNonpositiveDelta,
                globalGuard = HitStopService.FeelCostVisualGlobalGuard, localAdjusted = HitStopService.FeelCostVisualLocalAdjusted, offSkip = HitStopService.FeelCostVisualOffSkip };
            _window.cpuMeanMs = _window.cpuTotalNs / (1000000d * Math.Max(1, _window.samples));
            ResetPresentation();
        }
        private void MixAux(int value) { _auxHash = (_auxHash ^ unchecked((uint)value)) * 16777619u; }
        private void SpawnCostTracer(int step)
        {
            int group = 500000 + step;
            bool sampled = FeelCostMeasurement.Sampling;
            if (sampled) { _window.tracerAttempts++; MixAux(step); MixAux(group); MixAux(160); MixAux(1000); }
            HitStopService.BeginSpawnGroup(group);
            try { HitFeedbackDirector.Instance.ReportTracer(_origin + Vector3.up, _origin + Vector3.up + Vector3.forward * 4f, .01f, 1f); }
            finally { HitStopService.EndSpawnGroup(); }
            bool spawned = false;
            for (int i = 0; i < _tracers.Length; i++)
                if (_tracers[i] != null && _tracers[i].IsSpawned && _tracers[i].LocalTimeGroupId == group) { spawned = true; break; }
            if (sampled && spawned) _window.tracerSpawns++;
            // Exact group matches a real pooled effect. Local branch occurrence is observed, never fabricated.
            FeelEventRouter.Enqueue(ImpactFeelId.CriticalHit, group, _origin, Vector3.forward);
        }
        private void DriveNaturalRound(int step)
        {
            if (step % 6 != 0 || RunDirector.Instance.Round != _window.before.round || RunDirector.Instance.Phase != RunPhase.Fighting) return;
            ZombieEnemy victim = null;
            for (int i = 0; i < ZombieEnemy.Live.Count; i++)
            {
                ZombieEnemy z = ZombieEnemy.Live[i];
                if (z != null && z.IsAlive && (victim == null || z.LifeId < victim.LifeId)) victim = z;
            }
            MixAux(step);
            if (victim == null) { MixAux(-1); return; }
            _window.killInputs++; MixAux(victim.LifeId - _naturalFirstLife); MixAux((int)victim.Tier); MixAux(Mathf.RoundToInt(victim.MaxHealth * 100f));
            var hit = new DamageHit { Amount = 1000000f, Region = BodyRegion.UpperBody, Point = _origin + Vector3.up,
                Direction = Vector3.forward, ImpactForce = 1f, UngovernedImpactForce = 1f, Source = DamageSourceKind.Bullet };
            DamageHitContext.Set(1f, 1f, true, ClassicWeaponId.AssaultRifle, null);
            try
            {
                // First may break armour; second naturally removes health. No Kill/DebugDrain/DebugComplete/private handler.
                for (int i = 0; i < 2; i++)
                {
                    _window.damageInputs++; MixAux(i); DamageResult r = victim.ApplyHit(in hit);
                    MixAux(r.Killed ? 1 : 0); MixAux(r.ArmorBroke ? 1 : 0); MixAux(Mathf.RoundToInt(r.HealthDamage * 100f));
                }
            }
            finally { DamageHitContext.Clear(); }
        }
        private void ObserveDeviceFrame()
        {
            var prefs = InputPreferencesStore.Current;
            FeelCostDevices.Read(out int id, out string layout, out _, out bool native);   // id 0 = no pad
            if (id != _window.deviceId || (id != 0 && layout != _window.deviceLayout)) _window.deviceChanges++;
            if (native) _window.nativeDeviceFrames++;
            if (prefs.RumbleEnabled != _window.initialRumbleEnabled || prefs.RumbleIntensity != _window.initialIntensity || FeelProfileTD.Active.rumble != _window.initialProfileRumble)
                _window.preferenceStable = false;
            if (native && !RunFlowClock.IsFrozen && prefs.RumbleEnabled && FeelProfileTD.Active.rumble) _window.eligibleFrames++;
        }
        private static StateProof State()
        {
            var d = RunDirector.Instance; var spawn = EnemySpawnDirector.Instance; var stats = d.Stats; var report = d.LastRoundReport;
            return new StateProof { round = d.Round, phase = (int)d.Phase, ledgerState = (int)spawn.State, poolTotal = spawn.RoundPoolTotal, pool = spawn.RemainingInPool, alive = spawn.ActiveCount,
                killed = spawn.KilledThisRound, spawned = spawn.SpawnedThisRound, completedRounds = stats.CompletedRounds, totalKills = stats.TotalKills,
                totalXp = stats.TotalXp, roundXp = stats.CompletedRoundXp, expectedRoundXp = PrototypeConfig.Active != null ? PrototypeConfig.Active.RoundCompletionXp(d.Round) : 0, goldIncome = stats.GoldEarned, wallet = d.Wallet != null ? d.Wallet.Balance : 0,
                reportRound = report.Round, reportKills = report.Kills, reportGold = report.GoldIncome, frozen = RunFlowClock.IsFrozen,
                roundHold = RunFlowClock.HasHold(RunFlowClock.RoundEndHold) };
        }
        internal void EndRenderedFrame()
        {
            if (!_collecting || (_step == 0 && !FeelCostMeasurement.Sampling)) return;
            if (_pending >= 0)
            {
                Row prev = _rows[_pending]; prev.cpuNs = FeelCostMeasurement.PreviousCpuNs(out prev.cpuSamples);
                if (prev.cpuSamples != prev.roots || prev.cpuNs < 0) prev.errors |= 16;
                _rows[_pending] = prev; _window.cpuTotalNs += Math.Max(0, prev.cpuNs);
                _window.cpuMaxNs = Math.Max(_window.cpuMaxNs, prev.cpuNs); _window.errors |= prev.errors;
            }
            if (!FeelCostMeasurement.Sampling)
            { _pending = -1; _collecting = false; _done = true; return; }
            int crowd = EnemySpawnDirector.Instance != null ? EnemySpawnDirector.Instance.ActiveCount : -1;
            var row = new Row { window = _windowIndex, step = _step - 1, frame = Time.frameCount,
                roots = FeelCostMeasurement.FrameRoots, gcSamples = FeelCostMeasurement.FrameGcSamples,
                gcBytes = FeelCostMeasurement.FrameGcBytes, stopwatchNs = FeelCostMeasurement.FrameStopwatchNs,
                overheadNs = FeelCostMeasurement.FrameInstrumentationNs, crowd = crowd, errors = FeelCostMeasurement.ErrorBits,
                phase = (int)RunDirector.Instance.Phase, pool = EnemySpawnDirector.Instance.RemainingInPool, killed = EnemySpawnDirector.Instance.KilledThisRound,
                tracerAttempts = _window.tracerAttempts, tracerSpawns = _window.tracerSpawns, deviceId = FeelCostDevices.PadId,
                eligible = _window.eligibleFrames, visualInactive = HitStopService.FeelCostVisualInactive, visualZero = HitStopService.FeelCostVisualGroupZero,
                visualMismatch = HitStopService.FeelCostVisualMismatch, visualGlobal = HitStopService.FeelCostVisualGlobalGuard,
                visualLocal = HitStopService.FeelCostVisualLocalAdjusted, visualOff = HitStopService.FeelCostVisualOffSkip, visualNonpositive = HitStopService.FeelCostVisualNonpositiveDelta };
            if (_rowCount >= _rows.Length) { Fail("raw frame capacity exceeded"); _collecting = false; _done = true; return; }
            _pending = _rowCount; _rows[_rowCount++] = row;
            for (int i = 0; i < (int)FeelCostPath.Count; i++) _pathRows[_pending * (int)FeelCostPath.Count + i] = FeelCostMeasurement.PathCalls[i];
            _window.samples++; _window.roots += row.roots; _window.gcSamples += row.gcSamples; _window.gcTotalB += row.gcBytes;
            _window.gcMaxB = (int)Math.Min(int.MaxValue, Math.Max(_window.gcMaxB, row.gcBytes)); _window.instrumentationNs += row.overheadNs;
            _window.crowdMin = Math.Min(_window.crowdMin, crowd); _window.crowdMax = Math.Max(_window.crowdMax, crowd);
        }
        private static void ResetPresentation()
        {
            FeelCostMeasurement.Sampling = false;
            FeelTimeScale.Cancel(); DeathSequence.Cancel(); FeelEventRouter.Clear();
            CameraFeelDriver.ResetFeelCostState();
            if (PrototypeCameraRig.Active != null) PrototypeCameraRig.Active.ResetFeelCostState();
            var installer = PrototypeInputInstaller.Instance;
            if (installer != null)
            {
                var field = typeof(PrototypeInputInstaller).GetField("_rumble", BindingFlags.Instance | BindingFlags.NonPublic);
                var rumble = field != null ? field.GetValue(installer) as GamepadRumble : null;
                if (rumble != null) rumble.ResetFeelCostState();
            }
        }
        private static string CrowdHash()
        {
            // Ordering-independent initial-position fingerprint. Not a guarantee of deterministic physics thereafter.
            uint hash = 0;
            foreach (var z in ZombieEnemy.Live) if (z != null && z.IsAlive)
            { Vector3 v = z.transform.position; hash += unchecked((uint)(Mathf.RoundToInt(v.x * 100f) * 73856093 ^ Mathf.RoundToInt(v.z * 100f) * 19349663)); }
            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
        private static bool SameInitial(StateProof a, StateProof b)
        {
            return a.round == b.round && a.phase == b.phase && a.ledgerState == b.ledgerState && a.poolTotal == b.poolTotal && a.pool == b.pool && a.alive == b.alive &&
                a.killed == b.killed && a.spawned == b.spawned && a.completedRounds == b.completedRounds && a.totalKills == b.totalKills &&
                a.totalXp == b.totalXp && a.roundXp == b.roundXp && a.wallet == b.wallet && a.goldIncome == b.goldIncome && a.frozen == b.frozen && a.roundHold == b.roundHold;
        }
        private static bool SameAfter(StateProof a, StateProof b)
        {
            return a.round == b.round && a.phase == b.phase && a.ledgerState == b.ledgerState && a.pool == b.pool && a.alive == b.alive && a.killed == b.killed &&
                a.completedRounds == b.completedRounds && a.totalKills == b.totalKills && a.totalXp == b.totalXp && a.roundXp == b.roundXp &&
                a.wallet == b.wallet && a.goldIncome == b.goldIncome && a.reportRound == b.reportRound && a.reportKills == b.reportKills && a.reportGold == b.reportGold &&
                a.frozen == b.frozen && a.roundHold == b.roundHold;
        }
        private static bool DurationValid(Window a, Window b)
        {
            return Math.Abs(a.actualSeconds - a.logicalSeconds) <= .5d && Math.Abs(b.actualSeconds - b.logicalSeconds) <= .5d && Math.Abs(a.actualSeconds - b.actualSeconds) <= .3d;
        }
        private static bool NaturalComplete(Window x)
        {
            return x.samples == RoundFrames && x.after.round == x.before.round && x.after.pool == 0 && x.after.alive == 0 &&
                x.after.ledgerState == (int)RoundState.Completed && x.after.killed == x.before.poolTotal &&
                x.after.completedRounds == x.before.completedRounds + 1 && x.after.totalKills == x.before.totalKills + x.before.poolTotal &&
                x.after.roundXp == x.before.roundXp + x.before.expectedRoundXp && x.after.reportRound == x.before.round && x.after.reportKills == x.before.poolTotal &&
                x.after.roundHold && x.after.frozen && x.roundObservation.ClearAccepted == 1 && x.roundObservation.ClearIgnored == 0 &&
                x.roundObservation.HoldApplied == 1 && x.killInputs == x.before.poolTotal && x.damageInputs == x.before.poolTotal * 2 &&
                x.eventCalls[(int)FeelEventKind.EnemyKilled] == x.before.poolTotal && x.eventCalls[(int)FeelEventKind.RoundEnded] == 1 &&
                x.eventCalls[(int)FeelEventKind.LastZombie] == 1 && x.externalEvents == 0;
        }
        private void Judge()
        {
            Window a = _result.off, b = _result.on, ra = _result.roundOff, rb = _result.roundOn;
            _result.errorBits = FeelCostMeasurement.ErrorBits | a.errors | b.errors | ra.errors | rb.errors;
            _result.callbackNames = (string[])FeelCostMeasurement.MemberNames.Clone();
            _result.expectedCallbacks = (bool[])FeelCostMeasurement.ExpectedMembers.Clone();
            _result.original95 = (bool[])FeelCostMeasurement.OriginalRequiredMembers.Clone();
            _result.originalRequiredCount = 0; for (int i = 0; i < _result.original95.Length; i++) if (_result.original95[i]) _result.originalRequiredCount++;
            int length = _result.callbackNames.Length;
            _result.callbackClasses = new string[length]; _result.steadyRequired = new bool[length]; _result.roundRequired = new bool[length];
            var missing = new System.Collections.Generic.List<string>();
            for (int i = 0; i < length; i++)
            {
                FeelCostCallbackClass c = FeelCostMeasurement.CallbackClasses[i]; _result.callbackClasses[i] = c.ToString();
                if (!_result.expectedCallbacks[i]) continue;
                _result.steadyRequired[i] = c == FeelCostCallbackClass.SteadyProduction || c == FeelCostCallbackClass.WarmObserved;
                _result.roundRequired[i] = c == FeelCostCallbackClass.RoundEndProduction;
                if (_result.steadyRequired[i] && b.callbackCalls[i] == 0) missing.Add("Steady:" + _result.callbackNames[i]);
                if (_result.roundRequired[i] && rb.callbackCalls[i] == 0) missing.Add("NaturalRound:" + _result.callbackNames[i]);
            }
            _result.missingCallbacks = missing.ToArray();
            _result.inputMatched = a.inputHash == b.inputHash && a.auxInputHash == b.auxInputHash && a.externalEvents == 0 && b.externalEvents == 0 &&
                a.initialCrowdHash == b.initialCrowdHash && a.criticalEvents == 60 && b.criticalEvents == 60 &&
                a.fireStarts == 11 && a.fireStops == 11 && b.fireStarts == 11 && b.fireStops == 11 && SameInitial(a.before, b.before);
            for (int i = 0; i < _result.eventsExpected.Length; i++)
                _result.inputMatched &= a.eventCalls[i] == _result.eventsExpected[i] && b.eventCalls[i] == _result.eventsExpected[i];
            _result.coverageComplete = a.samples == Frames && b.samples == Frames && a.roots > 0 && b.roots > 0 &&
                a.crowdMin == _crowd && a.crowdMax == _crowd && b.crowdMin == _crowd && b.crowdMax == _crowd && a.routerOverflow == 0 && b.routerOverflow == 0 &&
                a.tracerAttempts == 10 && b.tracerAttempts == 10 && a.tracerSpawns == 10 && b.tracerSpawns == 10 && b.visualBranches.localAdjusted > 0 && a.visualBranches.offSkip > 0 && missing.Count == 0;
            _result.productionDeviceMatched = a.deviceId != 0 && a.deviceId == b.deviceId && a.deviceLayout == b.deviceLayout &&
                a.rumbleOwnerAvailable && b.rumbleOwnerAvailable && a.initialNativeDevice && b.initialNativeDevice && a.deviceChanges == 0 && b.deviceChanges == 0 && a.preferenceStable && b.preferenceStable &&
                a.initialRumbleEnabled && b.initialRumbleEnabled && a.initialProfileRumble && b.initialProfileRumble && a.initialIntensity == b.initialIntensity &&
                a.eligibleFrames == Frames && b.eligibleFrames == Frames && a.nativeDeviceFrames == Frames && b.nativeDeviceFrames == Frames;
            int sampleSlot = Array.IndexOf(_result.callbackNames, "RumbleMixer.Sample");
            _result.productionDeviceMatched &= sampleSlot >= 0 && b.callbackCalls[sampleSlot] == Frames && a.callbackCalls[sampleSlot] == 0;
            _result.productionRumbleVerdict = _result.productionDeviceMatched ? "PRODUCTION_MIXER_OBSERVED_BACKEND_MANUAL_OPEN" : "UNMEASURED";
            _result.roundInputMatched = ra.inputHash == rb.inputHash && ra.auxInputHash == rb.auxInputHash && ra.initialCrowdHash == rb.initialCrowdHash && SameInitial(ra.before, rb.before);
            for (int i = 0; i < ra.eventCalls.Length; i++) _result.roundInputMatched &= ra.eventCalls[i] == rb.eventCalls[i];
            _result.roundStateMatched = SameAfter(ra.after, rb.after) && NaturalComplete(ra) && NaturalComplete(rb) &&
                ra.roundObservation.DeferFalse == 1 && rb.roundObservation.DeferTrue == 1 && rb.roundObservation.WaitingTicks > 0 &&
                rb.roundObservation.EnvelopeCompletions + rb.roundObservation.SafetyCompletions == 1;
            _result.roundDeltaCpuMs = rb.cpuMeanMs - ra.cpuMeanMs;
            bool roundValid = _result.provenanceMatched && _result.cpuControlPass && _result.gcControlPass && _result.errorBits == 0 &&
                _result.roundInputMatched && _result.roundStateMatched && ra.errors == 0 && rb.errors == 0 && DurationValid(ra, rb);
            for (int i = 0; i < length; i++) if (_result.roundRequired[i]) roundValid &= rb.callbackCalls[i] > 0;
            if (roundValid)
            {
                _result.roundCpuVerdict = _result.roundDeltaCpuMs <= .3d ? "PASS_SCOPED" : "FAIL_SCOPED";
                _result.roundGcVerdict = rb.gcSamples == 0 && rb.gcTotalB == 0 && rb.gcMaxB == 0 ? "PASS_SCOPED" : "FAIL_SCOPED";
                _result.roundVerdict = _result.roundCpuVerdict == "PASS_SCOPED" && _result.roundGcVerdict == "PASS_SCOPED" ? "PASS_SCOPED" : "FAIL_SCOPED";
            }
            int requestSlot = Array.IndexOf(_result.callbackNames, "LegacyHitStop.Request");
            Window ca = _result.compatibilityOff, cb = _result.compatibilityOn;
            if (requestSlot >= 0 && ca.compatibilityCalls == 4 && cb.compatibilityCalls == 4 && ca.callbackCalls[requestSlot] == 4 && cb.callbackCalls[requestSlot] == 4 &&
                ca.auxInputHash == cb.auxInputHash && ca.errors == 0 && cb.errors == 0) _result.compatibilityVerdict = "CONTROL_OBSERVED_NOT_PRODUCTION_ACCEPTANCE";
            if (_result.dspOnly != null) _result.dspVerdict = _result.provenanceMatched && _result.cpuControlPass && _result.gcControlPass && sampleSlot >= 0 && _result.dspOnly.callbackCalls[sampleSlot] == 4 && _result.dspOnly.samples == 4 && _result.dspOnly.roots == 4 && _result.dspOnly.errors == 0 && _result.dspOnly.gcTotalB == 0 ? "DSP_ONLY_OBSERVED_DEVICE_BACKEND_OPEN" : "UNMEASURED";
            _result.durationMatched = DurationValid(a, b);
            _result.deltaCpuMs = b.cpuMeanMs - a.cpuMeanMs;
            bool valid = _result.originalRequiredCount == 95 && _result.provenanceMatched && _result.errorBits == 0 && _result.cpuControlPass && _result.gcControlPass &&
                _result.inputMatched && _result.coverageComplete && _result.durationMatched && _result.productionDeviceMatched && roundValid;
            if (!valid) { _result.error = "production scope/control/input/coverage/device/round-state/duration/provenance incomplete; inspect phase raw fields"; return; }
            _result.cpuVerdict = _result.deltaCpuMs <= .3d && _result.roundDeltaCpuMs <= .3d ? "PASS_SCOPED" : "FAIL_SCOPED";
            _result.gcVerdict = b.gcSamples == 0 && b.gcTotalB == 0 && b.gcMaxB == 0 && rb.gcSamples == 0 && rb.gcTotalB == 0 && rb.gcMaxB == 0 ? "PASS_SCOPED" : "FAIL_SCOPED";
            _result.verdict = _result.cpuVerdict == "PASS_SCOPED" && _result.gcVerdict == "PASS_SCOPED" ? "PASS_SCOPED" : "FAIL_SCOPED";
        }
        private static bool IsFullSha(string value)
        {
            if (value == null || value.Length != 40) return false;
            foreach (char c in value) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return true;
        }
        private void Fail(string reason) { _result.error = reason; Save(); }
        private void Save()
        {
            if (_saved || string.IsNullOrEmpty(_out)) return;
            _saved = true; _result.errorBits |= FeelCostMeasurement.ErrorBits;
            FeelCostMeasurement.Sampling = FeelCostMeasurement.GateActive = false;
            ResetPresentation();
            if (FeelCostMeasurement.MemberNames != null)
            {
                _result.callbackNames = (string[])FeelCostMeasurement.MemberNames.Clone(); _result.expectedCallbacks = (bool[])FeelCostMeasurement.ExpectedMembers.Clone();
                _result.original95 = (bool[])FeelCostMeasurement.OriginalRequiredMembers.Clone();
                _result.originalRequiredCount = 0; for (int i = 0; i < _result.original95.Length; i++) if (_result.original95[i]) _result.originalRequiredCount++;
                _result.callbackClasses = new string[_result.callbackNames.Length];
                _result.steadyRequired = new bool[_result.callbackNames.Length]; _result.roundRequired = new bool[_result.callbackNames.Length];
                for (int i = 0; i < _result.callbackNames.Length; i++)
                {
                    FeelCostCallbackClass c = FeelCostMeasurement.CallbackClasses[i]; _result.callbackClasses[i] = c.ToString();
                    _result.steadyRequired[i] = _result.expectedCallbacks[i] && (c == FeelCostCallbackClass.SteadyProduction || c == FeelCostCallbackClass.WarmObserved);
                    _result.roundRequired[i] = _result.expectedCallbacks[i] && c == FeelCostCallbackClass.RoundEndProduction;
                }
            }
            File.WriteAllText(Path.Combine(_out, "feel-cost.json"), JsonUtility.ToJson(_result, true));
            using (var writer = new StreamWriter(Path.Combine(_out, "feel-cost.csv")))
            {
                writer.Write("window,step,frame,cpu_ns,cpu_samples,outer_scopes,gc_bytes,gc_samples,crowd,stopwatch_ns,instrumentation_ns,error_bits,phase,pool,killed,tracer_attempts,tracer_spawns,device_id,eligible_frames,visual_inactive,visual_zero,visual_mismatch,visual_global,visual_local,visual_off,visual_nonpositive");
                for (int p = 0; p < (int)FeelCostPath.Count; p++) writer.Write(",calls_" + ((FeelCostPath)p)); writer.WriteLine();
                for (int i = 0; i < _rowCount; i++)
                {
                    Row x = _rows[i]; writer.Write(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}",
                        x.window, x.step, x.frame, x.cpuNs, x.cpuSamples, x.roots, x.gcBytes, x.gcSamples, x.crowd, x.stopwatchNs, x.overheadNs, x.errors));
                    writer.Write(string.Format(CultureInfo.InvariantCulture, ",{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13}", x.phase, x.pool, x.killed,
                        x.tracerAttempts, x.tracerSpawns, x.deviceId, x.eligible, x.visualInactive, x.visualZero, x.visualMismatch, x.visualGlobal, x.visualLocal, x.visualOff, x.visualNonpositive));
                    for (int j = 0; j < (int)FeelCostPath.Count; j++) writer.Write("," + _pathRows[i * (int)FeelCostPath.Count + j]); writer.WriteLine();
                }
            }
            Cleanup();
#if !UNITY_EDITOR
            Application.Quit(_result.verdict == "PASS_SCOPED" ? 0 : _result.verdict == "FAIL_SCOPED" ? 2 : 3);
#endif
        }
        private static void OnLog(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) FeelCostMeasurement.Fault();
        }
        private void Cleanup()
        {
            _collecting = false; Application.logMessageReceived -= OnLog; FeelCostMeasurement.Disarm();
            Application.targetFrameRate = _oldCap; QualitySettings.vSyncCount = _oldVsync;
            UnityEngine.Profiling.Profiler.enabled = _oldProfiler;
            Time.timeScale = _oldScale; Time.fixedDeltaTime = _oldFixed;
        }
        private void OnApplicationQuit() { if (!_saved) { _result.error = "interrupted before complete pair"; Save(); } }
        private void OnDestroy() { if (!_saved) { _result.error = "agent destroyed"; Save(); } }
    }
    [DefaultExecutionOrder(32760)]
    internal sealed class FeelCostTail : MonoBehaviour
    {
        internal FeelCostAgent Owner;
        private void LateUpdate() { if (Owner != null) Owner.EndRenderedFrame(); }
    }
}
#endif
