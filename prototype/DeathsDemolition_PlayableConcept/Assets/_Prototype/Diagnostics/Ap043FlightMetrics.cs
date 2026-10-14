#if DEVELOPMENT_BUILD || UNITY_EDITOR || DD_PERF_PROBE
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using DD.Prototype.Feedback.Vfx;
using UnityEngine;
using UnityEngine.Profiling;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// Opt-in native Player diagnostic. No flag means no carrier, recorder, arrays or output.
    /// CPU times are scoped wall times, never GPU timings. GC.Alloc is an event count, not bytes.
    /// Only a valid 64 KiB positive control, adequate actual selected-flight draw coverage and
    /// non-overlapping scopes can support the inference that zero allocation events means 0 B.
    /// </summary>
    public static class Ap043FlightMetrics
    {
        private const string Flag = "-ddAp043FlightMetrics";
        private const string PackageRoot = "/Users/Erdem/Dev/KADRO/context/projects/deaths-demolition/asset-production/AP-043";
        private const int PhaseCount = 3;
        private const int MinimumDrawFrames = 30;
        private const long MinimumDrawInstances = 100;
        private const long MinimumPhaseCalls = 100;
        private static bool _armed, _sampling, _saved, _recorderValid, _controlValid, _windowComplete;
        private static string _out, _pilotOut, _warmupFile, _endFile, _caseAsked;
        private static Recorder _alloc;
        private static Phase[] _phases;
        private static int _depth, _phase, _ownerThread, _frame = -1, _drawFrame = -1;
        private static int _conflicts, _mispaired, _controlEvents = -1, _recordFailures;
        private static long _startedTicks, _frameTicks, _frameEvents, _measuredFrames, _drawFrames, _drawCalls, _instances;
        private static long _frameTicksSum, _frameTicksMax, _frameEventsMax;
        private static int _firstEngineFrame = -1, _lastEngineFrame = -1;
        private static float _openedAt, _closedAt;
        private static byte[] _controlBait;
        private static FrameTiming[] _timings;
        private static bool _gpuFeature;
        private static int _gpuSamples;
        private static double _gpuSum, _gpuMax;
        private static ulong _lastGpuStamp;

        private struct Phase
        {
            public long calls, ticks, maxTicks, events, maxEvents;
        }

        [Serializable]
        private sealed class PhaseReport
        {
            public string name, cpuStatus, gcStatus;
            public long calls, allocationEvents, maximumEventsPerCall;
            public double meanCpuMs, maximumCpuMs;
            public bool zeroBytesInferenceValid;
            public long zeroBytesInference = -1;
        }

        [Serializable]
        private sealed class Report
        {
            public string schema = "AP043-native-flight-metrics-v1";
            public string status, scope, output, pilotOutput, requestedCase, graphicsApi, graphicsDevice;
            public string cpuMethod = "Stopwatch.GetTimestamp around the synchronous method body; recorder setup/teardown excluded";
            public string gcMethod = "Unity GC.Alloc Recorder current-thread sampleBlockCount; bytes inferred only for validated zero events";
            public string gpuMethod = "FrameTimingManager complete frame, not projectile GPU cost";
            public bool nativePlayer, recorderValid, positiveControlValid, sufficientSelectedDrawCoverage, scopeIntegrity, windowComplete;
            public bool gpuFeatureEnabled, zeroBytesInferenceValid, wholeFrameGcClaim = false, projectileGpuClaim = false;
            public string gpuStatus;
            public int positiveControlBytes = 65536, positiveControlEvents, currentLookValue, conflicts, mismatchedScopes, recorderFailures;
            public int firstEngineFrame, lastEngineFrame, frameWidth, frameHeight, gpuSamples;
            public long measuredFrames, selectedDrawFrames, selectedDrawCalls, selectedDrawInstances;
            public double elapsedSeconds, meanScopedCpuPerFrameMs, maximumScopedCpuPerFrameMs, gpuFrameMeanMs, gpuFrameMaxMs;
            public long maximumAllocationEventsPerMeasuredFrame, zeroBytesInference = -1;
            public int requiredDrawFrames = MinimumDrawFrames;
            public long requiredDrawInstances = MinimumDrawInstances, requiredPhaseCalls = MinimumPhaseCalls;
            public PhaseReport[] phases;
            public string[] limits;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _armed = _sampling = _saved = _recorderValid = _controlValid = _windowComplete = false;
            _out = _pilotOut = _warmupFile = _endFile = _caseAsked = null;
            _alloc = null; _phases = null; _controlBait = null; _timings = null;
            _depth = _phase = _conflicts = _mispaired = _recordFailures = _gpuSamples = 0;
            _ownerThread = 0; _controlEvents = -1; _frame = _drawFrame = -1;
            _startedTicks = _frameTicks = _frameEvents = _measuredFrames = _drawFrames = _drawCalls = _instances = 0;
            _frameTicksSum = _frameTicksMax = _frameEventsMax = 0;
            _firstEngineFrame = _lastEngineFrame = -1; _openedAt = _closedAt = 0f;
            _gpuFeature = false; _gpuSum = _gpuMax = 0d; _lastGpuStamp = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            string requested = Arg(args, Flag);
            if (requested == null) return;
            try
            {
                _out = CheckedDirectory(requested);
                _pilotOut = CheckedDirectory(Arg(args, "-ddPilotPerfProbe"));
                _warmupFile = Path.Combine(_pilotOut, "03-warmup.json");
                _endFile = Path.Combine(_pilotOut, "04-window.json");
                if (File.Exists(_warmupFile) || File.Exists(_endFile) || File.Exists(Path.Combine(_out, "ap043-flight-metrics.json")))
                    throw new IOException("fresh metrics and Pilot evidence directories are required");
                _caseAsked = Arg(args, "-ddAp043Case");
                _phases = new Phase[PhaseCount];
                _timings = new FrameTiming[1];
                _ownerThread = Thread.CurrentThread.ManagedThreadId;
                _alloc = Recorder.Get("GC.Alloc");
                _recorderValid = _alloc != null && _alloc.isValid;
                if (_recorderValid)
                {
                    // First bait warms the exact allocation recorder path; the second is the control.
                    Control(false);
                    _controlEvents = Control(true);
                    _controlValid = _controlEvents > 0 && _recordFailures == 0;
                }
                _gpuFeature = FrameTimingManager.IsFeatureEnabled();
                _armed = true;
                var carrier = new GameObject("AP043 Flight Metrics (opt-in)");
                UnityEngine.Object.DontDestroyOnLoad(carrier);
                carrier.AddComponent<Observer>();
                UnityEngine.Debug.Log("[AP043FlightMetrics] armed; positive control events=" + _controlEvents + "; output=" + _out);
            }
            catch (Exception ex)
            {
                _armed = _sampling = false;
                DisableRecorder();
                UnityEngine.Debug.LogError("[AP043FlightMetrics] fail closed: " + ex.Message);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Control(bool keepAlive)
        {
            int count = -1;
            try
            {
                _alloc.enabled = false;
                _alloc.FilterToCurrentThread();
                _alloc.enabled = true;
                _controlBait = new byte[65536];
                _controlBait[0] = 43;
                _controlBait[65535] = 2;
            }
            catch (Exception) { _recordFailures++; }
            finally
            {
                try
                {
                    _alloc.enabled = false;
                    count = _alloc.sampleBlockCount;
                    _alloc.CollectFromAllThreads();
                }
                catch (Exception) { _recordFailures++; }
                if (keepAlive) GC.KeepAlive(_controlBait);
            }
            return count;
        }

        /// <summary>0 Tick, 1 DrawTd, 2 DrawTdInk. The caller must use try/finally.</summary>
        public static void BeginHotScope(int phase)
        {
            if (!_sampling) return;
            if ((uint)phase >= PhaseCount) { _mispaired++; return; }
            if (_ownerThread != Thread.CurrentThread.ManagedThreadId) { _conflicts++; return; }
            if (_depth != 0) { _depth++; _conflicts++; return; }
            AdvanceFrame();
            _depth = 1;
            _phase = phase;
            if (_recorderValid)
            {
                try
                {
                    _alloc.enabled = false;
                    _alloc.FilterToCurrentThread();
                    _alloc.enabled = true;
                }
                catch (Exception) { _recordFailures++; _recorderValid = false; }
            }
            _startedTicks = Stopwatch.GetTimestamp();
        }

        public static void EndHotScope(int phase)
        {
            if (!_sampling) return;
            if (_depth <= 0) { _mispaired++; return; }
            if (_depth > 1) { _depth--; return; }
            long elapsed = Stopwatch.GetTimestamp() - _startedTicks;
            if (phase != _phase) _mispaired++;
            int count = -1;
            if (_alloc != null && _recorderValid)
            {
                try
                {
                    _alloc.enabled = false;
                    count = _alloc.sampleBlockCount;
                    _alloc.CollectFromAllThreads();
                }
                catch (Exception) { _recordFailures++; _recorderValid = false; }
            }
            _depth = 0;
            ref Phase p = ref _phases[_phase];
            p.calls++; p.ticks += elapsed;
            if (elapsed > p.maxTicks) p.maxTicks = elapsed;
            _frameTicks += elapsed;
            if (count >= 0)
            {
                p.events += count;
                if (count > p.maxEvents) p.maxEvents = count;
                _frameEvents += count;
            }
        }

        /// <summary>Call only after an actual AP043 selected-flight DrawMeshInstanced submission.</summary>
        public static void RecordDraw(int instanceCount)
        {
            if (!_sampling || instanceCount <= 0) return;
            AdvanceFrame();
            _drawCalls++;
            _instances += instanceCount;
            if (_drawFrame != Time.frameCount) { _drawFrames++; _drawFrame = Time.frameCount; }
        }

        private static void AdvanceFrame()
        {
            int f = Time.frameCount;
            if (f == _frame) return;
            FlushFrame();
            _frame = f;
            if (_firstEngineFrame < 0) _firstEngineFrame = f;
            _lastEngineFrame = f;
        }

        private static void FlushFrame()
        {
            if (_frame < 0) return;
            _measuredFrames++;
            _frameTicksSum += _frameTicks;
            if (_frameTicks > _frameTicksMax) _frameTicksMax = _frameTicks;
            if (_frameEvents > _frameEventsMax) _frameEventsMax = _frameEvents;
            _frameTicks = _frameEvents = 0;
            _frame = -1;
        }

        private static void DisableRecorder()
        {
            if (_alloc == null) return;
            try { _alloc.enabled = false; _alloc.CollectFromAllThreads(); }
            catch (Exception) { _recordFailures++; }
        }

        private sealed class Observer : MonoBehaviour
        {
            private float _nextPoll;
            private void Update()
            {
                if (!_armed || _saved) return;
                // File polling is outside all hot scopes and uses strings prepared at startup.
                if (Time.realtimeSinceStartup >= _nextPoll)
                {
                    _nextPoll = Time.realtimeSinceStartup + .1f;
                    if (!_sampling && _openedAt == 0f && File.Exists(_warmupFile))
                    {
                        _openedAt = Time.realtimeSinceStartup;
                        _sampling = true;
                    }
                    if (_sampling && File.Exists(_endFile))
                    {
                        _closedAt = Time.realtimeSinceStartup;
                        _windowComplete = true;
                        _sampling = false;
                        DisableRecorder();
                        FlushFrame();
                    }
                }
                if (!_sampling) return;
                if (_depth != 0) { _conflicts++; return; }
                AdvanceFrame();
                if (!_gpuFeature) return;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _timings) == 0) return;
                FrameTiming t = _timings[0];
                if (t.frameStartTimestamp == _lastGpuStamp || t.gpuFrameTime <= 0d || double.IsNaN(t.gpuFrameTime) || double.IsInfinity(t.gpuFrameTime)) return;
                _lastGpuStamp = t.frameStartTimestamp;
                _gpuSamples++; _gpuSum += t.gpuFrameTime;
                if (t.gpuFrameTime > _gpuMax) _gpuMax = t.gpuFrameTime;
            }

            private void OnApplicationQuit() { Save(); }
        }

        private static void Save()
        {
            if (!_armed || _saved) return;
            _saved = true;
            _sampling = false;
            if (_depth != 0) _conflicts++;
            DisableRecorder();
            FlushFrame();
            if (_closedAt == 0f) _closedAt = Time.realtimeSinceStartup;
            bool coverage = (int)Ap043BulletLooks.Current == 2 && _drawFrames >= MinimumDrawFrames && _instances >= MinimumDrawInstances;
            bool integrity = _depth == 0 && _conflicts == 0 && _mispaired == 0 && _ownerThread == Thread.CurrentThread.ManagedThreadId;
            bool baseValid = !Application.isEditor && _windowComplete && _recorderValid && _controlValid && integrity && coverage && _recordFailures == 0;
            bool zero = baseValid;
            var rows = new PhaseReport[PhaseCount];
            string[] names = { "Tick", "DrawTd", "DrawTdInk" };
            for (int i = 0; i < PhaseCount; i++)
            {
                Phase p = _phases[i];
                bool observed = baseValid && p.calls >= MinimumPhaseCalls;
                bool phaseZero = observed && p.events == 0;
                rows[i] = new PhaseReport { name = names[i], calls = p.calls,
                    cpuStatus = p.calls > 0 && integrity ? "MEASURED_SCOPED_CPU" : "UNAVAILABLE",
                    meanCpuMs = p.calls > 0 ? Milliseconds(p.ticks) / p.calls : 0d, maximumCpuMs = Milliseconds(p.maxTicks),
                    allocationEvents = p.events, maximumEventsPerCall = p.maxEvents,
                    gcStatus = !observed ? "UNAVAILABLE" : phaseZero ? "ZERO_EVENTS_VALIDATED" : "ALLOCATION_EVENTS_OBSERVED",
                    zeroBytesInferenceValid = phaseZero, zeroBytesInference = phaseZero ? 0 : -1 };
                zero &= phaseZero;
            }
            var report = new Report { output = _out, pilotOutput = _pilotOut, requestedCase = _caseAsked,
                nativePlayer = !Application.isEditor, status = !baseValid ? "UNAVAILABLE" : zero ? "SCOPED_ZERO_EVENTS_VALIDATED" : "ALLOCATION_EVENTS_OR_INSUFFICIENT_PHASE_COVERAGE",
                scope = "After 03-warmup.json is observed until 04-window.json is observed. Includes Pilot's 1-second heap subwindow and at most 0.1-second polling latency. Not exactly the Pilot frame-time CSV window.",
                recorderValid = _recorderValid, positiveControlValid = _controlValid, positiveControlEvents = _controlEvents, windowComplete = _windowComplete,
                sufficientSelectedDrawCoverage = coverage, scopeIntegrity = integrity, conflicts = _conflicts, mismatchedScopes = _mispaired, recorderFailures = _recordFailures,
                currentLookValue = (int)Ap043BulletLooks.Current, firstEngineFrame = _firstEngineFrame, lastEngineFrame = _lastEngineFrame,
                measuredFrames = _measuredFrames, selectedDrawFrames = _drawFrames, selectedDrawCalls = _drawCalls, selectedDrawInstances = _instances,
                elapsedSeconds = _openedAt > 0f ? _closedAt - _openedAt : 0d,
                meanScopedCpuPerFrameMs = _measuredFrames > 0 ? Milliseconds(_frameTicksSum) / _measuredFrames : 0d,
                maximumScopedCpuPerFrameMs = Milliseconds(_frameTicksMax), maximumAllocationEventsPerMeasuredFrame = _frameEventsMax,
                zeroBytesInferenceValid = zero, zeroBytesInference = zero ? 0 : -1,
                frameWidth = Screen.width, frameHeight = Screen.height, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), graphicsDevice = SystemInfo.graphicsDeviceName,
                gpuFeatureEnabled = _gpuFeature, gpuSamples = _gpuSamples, gpuStatus = _gpuSamples > 0 ? "MEASURED_WHOLE_FRAME_ONLY" : "UNAVAILABLE",
                gpuFrameMeanMs = _gpuSamples > 0 ? _gpuSum / _gpuSamples : 0d, gpuFrameMaxMs = _gpuMax,
                phases = rows, limits = new[] { "No whole-frame GC claim; other systems and threads are excluded.", "CPU is diagnostic scoped wall time; this is not a GPU projectile-cost or frame-budget verdict.", "Nonzero GC samples are counts only. Their byte size is unknown.", "FrameTimingStats disabled or zero GPU samples yields UNAVAILABLE; ProjectSettings are not modified.", "No renderer can be proven zero-allocation without a working positive control and adequate actual O2 draw coverage." } };
            try
            {
                // Recheck containment/reparse points at the actual write; never replace prior evidence.
                string directory = CheckedDirectory(_out);
                using (var stream = new FileStream(Path.Combine(directory, "ap043-flight-metrics.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(report, true));
            }
            catch (Exception ex) { UnityEngine.Debug.LogError("[AP043FlightMetrics] output refused: " + ex.Message); }
        }

        private static double Milliseconds(long ticks) => ticks * (1000d / Stopwatch.Frequency);
        private static string Arg(string[] args, string key)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key && !args[i + 1].StartsWith("-", StringComparison.Ordinal)) return args[i + 1];
            return null;
        }

        private static string CheckedDirectory(string requested)
        {
            if (string.IsNullOrEmpty(requested) || !Path.IsPathRooted(requested)) throw new IOException("absolute AP043 output directory required");
            string full = Path.GetFullPath(requested).TrimEnd(Path.DirectorySeparatorChar);
            string validation = PackageRoot + "/validation", renders = PackageRoot + "/06-renders";
            if (!(full == validation || full.StartsWith(validation + "/", StringComparison.Ordinal) || full == renders || full.StartsWith(renders + "/", StringComparison.Ordinal)))
                throw new IOException("only AP043 validation/ or 06-renders/ is allowed");
            string cursor = full;
            while (!string.IsNullOrEmpty(cursor))
            {
                if ((Directory.Exists(cursor) || File.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("symlink/reparse output path is refused");
                cursor = Path.GetDirectoryName(cursor);
            }
            Directory.CreateDirectory(full);
            return full;
        }
    }
}
#endif
