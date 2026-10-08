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
using DD.Prototype.Enemies;
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
        private const int Frames = 3600, WarmFrames = 900, Crowd = 60, Seed = 95060;
        [Serializable] private sealed class Window
        {
            public bool layersOn;
            public int samples, roots, gcSamples, gcMaxB, crowdMin = int.MaxValue, crowdMax;
            public long gcTotalB, cpuTotalNs, cpuMaxNs, instrumentationNs;
            public double cpuMeanMs, actualSeconds, logicalSeconds = Frames / 60d;
            public int externalEvents, criticalEvents, fireStarts, fireStops, errors, routerOverflow;
            public int[] pathCalls, eventCalls, callbackCalls;
            public string inputHash, initialCrowdHash;
        }
        [Serializable] private sealed class Result
        {
            public string kind = "synthetic-ingress-same-build-main-thread-feel-bundle";
            public string verdict = "UNMEASURED", cpuVerdict = "UNMEASURED", gcVerdict = "UNMEASURED";
            public string sourceSha, buildSha, buildStamp, error = "", scopeVersion = "wp095.cost.v1";
            public int seed = Seed, crowd = Crowd, attempt, nominalHz = 60, framesPerWindow = Frames;
            public string cpuMarker = FeelCostMeasurement.MarkerName, gcMarker = "GC.Alloc";
            public bool cpuRecorderValid, gcRecorderValid, cpuControlPass, gcControlPass, inputMatched, coverageComplete, durationMatched, provenanceMatched;
            public long cpuControlNs, cpuControlSamples, gcControlBytes;
            public int gcControlSamples, errorBits;
            public double deltaCpuMs;
            public string threadScope = "Synchronous outer main-thread scopes; GC bytes summed per rendered frame; not whole Player GC";
            public string cpuRule = "mean inclusive scoped ns/frame ON minus OFF <=0.3ms; nested scopes counted once; no overhead subtraction";
            public string exclusions = "Startup/lifecycle/reset/IO, unrelated base camera, worker/audio/render threads, GPU/native work outside listed calls";
            public string expandedScope = "Events includes synchronous subscribers; Router/LegacyHitStop/DeathSequence include existing shared presentation; not pure historical patch delta";
            public string durationRule = "3600 identical ordered input steps at requested60fps =60 logical seconds; actual windows must each be within0.5s of60s and within0.3s of each other";
            public string instrumentation = "GC recorder Reset/Start/Stop/read outside CPU marker; their stopwatch ns reported separately; nesting counters inside inclusive scope remain measured";
            public string[] paths, callbackNames, missingCallbacks;
            public string callbackRule = "Static required path/member inventory, ON coverage for every required callback; warm-observed extras also required; Zoom overloads have separate IDs; callback observation does not prove every authored branch/device variant";
            public bool[] expectedCallbacks;
            public int[] eventsExpected;
            public Window off, on;
            public bool gameplayPass = false, c3Pass = false, wholePlayerGcPass = false;
        }
        private struct Row
        {
            public int window, step, frame, roots, gcSamples, crowd, errors;
            public long cpuNs, cpuSamples, gcBytes, stopwatchNs, overheadNs;
        }
        private readonly Result _result = new Result();
        private Row[] _rows;
        private int[] _pathRows;
        private int _rowCount, _pending = -1, _step, _windowIndex;
        private bool _collecting, _done, _stopNext, _saved;
        private string _out;
        private Window _window;
        private PlayerRig _player;
        private Transform _target;
        private Vector3 _origin;
        private double _start;
        private int _oldCap, _oldVsync;
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
            go.AddComponent<FeelCostTail>().Owner = agent;
        }
        private IEnumerator Start()
        {
            _oldCap = Application.targetFrameRate; _oldVsync = QualitySettings.vSyncCount;
            _oldScale = Time.timeScale; _oldFixed = Time.fixedDeltaTime;
            Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            _rows = new Row[Frames * 2]; _pathRows = new int[Frames * 2 * (int)FeelCostPath.Count];
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
            _result.gcControlPass = _result.gcControlBytes >= 65536 && _result.gcControlSamples > 0;
            _bait = null; yield return null;
            if (!_result.cpuControlPass || !_result.gcControlPass || FeelCostMeasurement.ErrorBits != 0)
            { Fail("recorder positive control failed; no acceptance samples"); yield break; }
            for (int side = 0; side < 2; side++)
            {
                yield return PrepareScene(); if (_result.error.Length != 0) { Save(); yield break; }
                _windowIndex = side; _window = new Window { layersOn = side == 1 };
                FeelCostMeasurement.LayersOn = _window.layersOn; FeelCostMeasurement.GateActive = true;
                FeelCostMeasurement.RegistrationPhase = true;
                for (int i = 0; i < WarmFrames; i++) { Replay(i); yield return null; }
                Replay(Frames - 1); //warm the round-end/trigger-release callbacks too.
                FeelCostMeasurement.RegistrationPhase = false;
                ResetPresentation();
                _window.initialCrowdHash = CrowdHash();
                FeelCostMeasurement.ClearWindow(); _step = 0; _pending = -1; _done = _stopNext = false;
                _start = Time.realtimeSinceStartupAsDouble; _collecting = true;
                while (!_done) yield return null;
                FeelCostMeasurement.Sampling = false; FeelCostMeasurement.GateActive = false;
                _window.actualSeconds = Time.realtimeSinceStartupAsDouble - _start;
                _window.pathCalls = (int[])FeelCostMeasurement.PathCalls.Clone();
                _window.callbackCalls = (int[])FeelCostMeasurement.MemberCalls.Clone();
                _window.eventCalls = (int[])FeelCostMeasurement.EventCalls.Clone();
                _window.inputHash = FeelCostMeasurement.InputHash.ToString("x8", CultureInfo.InvariantCulture);
                _window.externalEvents = FeelCostMeasurement.ExternalEvents; _window.criticalEvents = FeelCostMeasurement.CriticalEvents;
                _window.fireStarts = FeelCostMeasurement.FireStarts; _window.fireStops = FeelCostMeasurement.FireStops;
                _window.errors |= FeelCostMeasurement.ErrorBits; _window.routerOverflow = FeelEventRouter.QueueOverflow;
                if (side == 0) _result.off = _window; else _result.on = _window;
                ResetPresentation();
            }
            Judge(); Save();
        }
        private IEnumerator PrepareScene()
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
            EnemySpawnDirector.Instance.ForceActiveTarget(Crowd, true);
            deadline = Time.realtimeSinceStartupAsDouble + 90d;
            while (EnemySpawnDirector.Instance.ActiveCount != Crowd && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (EnemySpawnDirector.Instance.ActiveCount != Crowd) { _result.error = "60 crowd unavailable"; yield break; }
            _target = null;
            foreach (var z in ZombieEnemy.Live) if (z != null && z.IsAlive) { _target = z.transform; break; }
            MuzzleSystem.Ensure(); CameraFeelDriver.Ensure();
            yield return null;
        }
        private void Update()
        {
            if (!_collecting) return;
            if (_stopNext) { FeelCostMeasurement.Sampling = false; return; }
            FeelCostMeasurement.BeginFrame(); FeelCostMeasurement.Sampling = true;
            Replay(_step++);
            if (_step == Frames) _stopNext = true;
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
            }
            finally { FeelCostMeasurement.Replaying = false; }
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
                overheadNs = FeelCostMeasurement.FrameInstrumentationNs, crowd = crowd, errors = FeelCostMeasurement.ErrorBits };
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
        private void Judge()
        {
            Window a = _result.off, b = _result.on;
            _result.errorBits = FeelCostMeasurement.ErrorBits | a.errors | b.errors;
            _result.callbackNames = (string[])FeelCostMeasurement.MemberNames.Clone();
            _result.expectedCallbacks = (bool[])FeelCostMeasurement.ExpectedMembers.Clone();
            _result.inputMatched = a.inputHash == b.inputHash && a.externalEvents == 0 && b.externalEvents == 0 &&
                a.initialCrowdHash == b.initialCrowdHash && a.criticalEvents == 60 && b.criticalEvents == 60 &&
                a.fireStarts == 11 && a.fireStops == 11 && b.fireStarts == 11 && b.fireStops == 11;
            for (int i = 0; i < _result.eventsExpected.Length; i++)
                _result.inputMatched &= a.eventCalls[i] == _result.eventsExpected[i] && b.eventCalls[i] == _result.eventsExpected[i];
            _result.coverageComplete = a.samples == Frames && b.samples == Frames && a.roots > 0 && b.roots > 0 &&
                a.crowdMin == Crowd && a.crowdMax == Crowd && b.crowdMin == Crowd && b.crowdMax == Crowd &&
                a.routerOverflow == 0 && b.routerOverflow == 0;
            for (int i = 0; i < (int)FeelCostPath.Control; i++) _result.coverageComplete &= b.pathCalls[i] > 0;
            for (int i = 0; i < _result.expectedCallbacks.Length; i++)
                if (_result.expectedCallbacks[i]) _result.coverageComplete &= b.callbackCalls[i] > 0;
            var missing = new System.Collections.Generic.List<string>();
            for (int i = 0; i < _result.expectedCallbacks.Length; i++)
                if (_result.expectedCallbacks[i] && b.callbackCalls[i] == 0) missing.Add(_result.callbackNames[i]);
            _result.missingCallbacks = missing.ToArray();
            _result.durationMatched = Math.Abs(a.actualSeconds - 60d) <= .5d && Math.Abs(b.actualSeconds - 60d) <= .5d && Math.Abs(a.actualSeconds - b.actualSeconds) <= .3d;
            a.cpuMeanMs = a.cpuTotalNs / (1000000d * Math.Max(1, a.samples)); b.cpuMeanMs = b.cpuTotalNs / (1000000d * Math.Max(1, b.samples));
            _result.deltaCpuMs = b.cpuMeanMs - a.cpuMeanMs;
            bool valid = _result.provenanceMatched && _result.errorBits == 0 && _result.cpuControlPass && _result.gcControlPass &&
                _result.inputMatched && _result.coverageComplete && _result.durationMatched;
            if (!valid) { _result.error = "scope/control/input/coverage/duration/provenance incomplete; inspect raw fields"; return; }
            _result.cpuVerdict = _result.deltaCpuMs <= .3d ? "PASS_SCOPED" : "FAIL_SCOPED";
            _result.gcVerdict = b.gcTotalB == 0 && b.gcMaxB == 0 ? "PASS_SCOPED" : "FAIL_SCOPED";
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
            { _result.callbackNames = (string[])FeelCostMeasurement.MemberNames.Clone(); _result.expectedCallbacks = (bool[])FeelCostMeasurement.ExpectedMembers.Clone(); }
            File.WriteAllText(Path.Combine(_out, "feel-cost.json"), JsonUtility.ToJson(_result, true));
            using (var writer = new StreamWriter(Path.Combine(_out, "feel-cost.csv")))
            {
                writer.Write("window,step,frame,cpu_ns,cpu_samples,outer_scopes,gc_bytes,gc_samples,crowd,stopwatch_ns,instrumentation_ns,error_bits");
                for (int p = 0; p < (int)FeelCostPath.Count; p++) writer.Write(",calls_" + ((FeelCostPath)p)); writer.WriteLine();
                for (int i = 0; i < _rowCount; i++)
                {
                    Row x = _rows[i]; writer.Write(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11}",
                        x.window, x.step, x.frame, x.cpuNs, x.cpuSamples, x.roots, x.gcBytes, x.gcSamples, x.crowd, x.stopwatchNs, x.overheadNs, x.errors));
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
