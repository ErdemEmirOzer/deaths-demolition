#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DD.Prototype.CameraSystem;
using Unity.Profiling;

namespace DD.Prototype.Feedback
{
    // This explicit bundle includes shared presentation at the listed entry points. Not whole-Player CPU/GPU.
    public enum FeelCostPath { Events, CameraDriver, CameraRig, CameraModel, Rumble, RumbleMixer, TimeScale, HitStop,
        Router, Muzzle, LegacyHitStop, DeathSequence, RunFlow, Control, Count }

    /// <summary>Opt-in QA only. Outermost synchronous main-thread scopes are inclusive, never summed twice.
    /// GC.Alloc is stopped/read synchronously per outer scope (NOT GC Allocated In Frame and NOT heap delta).
    /// Those byte samples are accumulated by rendered frame. Uncovered threads/native/render work is excluded.</summary>
    public enum FeelCostCallbackClass { SteadyProduction, RoundEndProduction, CompatibilityControl, WarmObserved }

    public static class FeelCostMeasurement
    {
        public const string MarkerName = "DD.Feel.Cost";
        public const int GcCapacity = 4096;
        private const int MembersPerPath = 64;
        private static Dictionary<string, int>[] s_members;
        public static string[] MemberNames { get; private set; }
        public static int[] MemberCalls { get; private set; }
        public static bool[] ExpectedMembers { get; private set; }
        public static bool[] OriginalRequiredMembers { get; private set; }
        public static FeelCostCallbackClass[] CallbackClasses { get; private set; }
        public static bool NaturalRoundPhase { get; set; }
        public static bool DiagnosticMixerOnly { get; set; }
        public static bool DspSampleCall { get; set; }
        public static int NaturalAsyncEvents { get; private set; }
        public static bool RegistrationPhase { get; set; }
        private static ProfilerMarker s_marker;
        private static ProfilerRecorder s_cpu, s_gc;
        private static bool s_armed;
        private static int s_thread, s_depth;
        private static long s_innerStart, s_outerStart;
        public static bool Sampling { get; set; }
        public static bool GateActive { get; set; }
        public static bool LayersOn { get; set; } = true;
        public static bool Replaying { get; set; }
        public static bool Armed => s_armed;
        public static bool CpuValid => s_armed && s_cpu.Valid;
        public static bool GcValid => s_armed && s_gc.Valid;
        public static int ErrorBits { get; private set; } // 1 recorder,2 thread,4 depth,8 GC overflow,16 CPU sample,32 cold/unregistered callback,64 callback table overflow,128 exception/error
        public static int ExternalEvents { get; private set; }
        public static int CriticalEvents { get; private set; }
        public static int FireStarts { get; private set; }
        public static int FireStops { get; private set; }
        public static long FrameGcBytes { get; private set; }
        public static int FrameGcSamples { get; private set; }
        public static int FrameRoots { get; private set; }
        public static long FrameStopwatchNs { get; private set; }
        public static long FrameInstrumentationNs { get; private set; }
        public static int[] PathCalls { get; private set; }
        public static int[] EventCalls { get; private set; }
        public static uint InputHash { get; private set; }
        private static long Ns(long ticks) => (long)(ticks * (1000000000d / Stopwatch.Frequency));

        public static void Arm()
        {
            if (s_armed) throw new InvalidOperationException("feel-cost already armed");
            ErrorBits = 0; s_depth = 0; RegistrationPhase = false;
            s_thread = Thread.CurrentThread.ManagedThreadId;
            PathCalls = new int[(int)FeelCostPath.Count]; EventCalls = new int[15];
            s_members = new Dictionary<string, int>[(int)FeelCostPath.Count];
            for (int i = 0; i < s_members.Length; i++) s_members[i] = new Dictionary<string, int>(MembersPerPath, StringComparer.Ordinal);
            MemberNames = new string[s_members.Length * MembersPerPath]; MemberCalls = new int[MemberNames.Length];
            ExpectedMembers = new bool[MemberNames.Length]; OriginalRequiredMembers = new bool[MemberNames.Length];
            CallbackClasses = new FeelCostCallbackClass[MemberNames.Length];
            RegisterRequiredCallbacks();
            s_marker = new ProfilerMarker(ProfilerCategory.Scripts, MarkerName);
            // CPU: previous complete frame, sum of all OUTER marker calls on this thread.
            s_cpu = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, MarkerName, 1,
                ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread | ProfilerRecorderOptions.SumAllSamplesInFrame);
            // Raw allocation events, no SumAllSamplesInFrame, no wrapping. Capacity saturation fails closed.
            s_gc = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", GcCapacity,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
            s_gc.Stop(); s_armed = true;
            if (!s_cpu.Valid || !s_gc.Valid) ErrorBits |= 1;
        }

        private static void Require(FeelCostPath path, string member, FeelCostCallbackClass classification = FeelCostCallbackClass.SteadyProduction)
        {
            var members = s_members[(int)path]; int slot = (int)path * MembersPerPath + members.Count;
            members.Add(member, slot); MemberNames[slot] = path + "." + member; ExpectedMembers[slot] = OriginalRequiredMembers[slot] = true; CallbackClasses[slot] = classification;
        }
        // Source inventory, not a list inferred from callbacks observed during warmup.
        // Scene lifecycle/reset callbacks are excluded; a scene change during sampling still invalidates the run.
        private static void RegisterRequiredCallbacks()
        {
            Require(FeelCostPath.Events, "Emit");
            Require(FeelCostPath.Events, "TickHeartbeat");
            Require(FeelCostPath.CameraDriver, "Update");
            Require(FeelCostPath.CameraDriver, "KillBeat");
            Require(FeelCostPath.CameraDriver, "OnShotTD");
            Require(FeelCostPath.CameraDriver, "OnFireStartTD");
            Require(FeelCostPath.CameraDriver, "OnFireStopTD");
            Require(FeelCostPath.CameraDriver, "OnReloadStartTD");
            Require(FeelCostPath.CameraDriver, "OnRoundEndTD");
            Require(FeelCostPath.CameraDriver, "OnHitTD");
            Require(FeelCostPath.CameraDriver, "OnKillTD");
            Require(FeelCostPath.CameraDriver, "OnMultiTD");
            Require(FeelCostPath.CameraDriver, "OnLastTD");
            Require(FeelCostPath.CameraDriver, "OnExplosionTD");
            Require(FeelCostPath.CameraDriver, "OnHurtTD");
            Require(FeelCostPath.CameraDriver, "OnRoundStartTD");
            Require(FeelCostPath.CameraRig, "ComputeTopDownFeelLean");
            Require(FeelCostPath.CameraRig, "ConsumeReactions");
            Require(FeelCostPath.CameraRig, "ApplyTopDownShake");
            Require(FeelCostPath.CameraModel, "Shot");
            Require(FeelCostPath.CameraModel, "FireHeld");
            Require(FeelCostPath.CameraModel, "World");
            Require(FeelCostPath.CameraModel, "Kick");
            Require(FeelCostPath.CameraModel, "Event");
            Require(FeelCostPath.CameraModel, "EventWithoutKick");
            Require(FeelCostPath.CameraModel, "Explosion");
            Require(FeelCostPath.CameraModel, "PlayerDamaged");
            Require(FeelCostPath.CameraModel, "LastZombie");
            Require(FeelCostPath.CameraModel, "RoundStart");
            Require(FeelCostPath.CameraModel, "Zoom.Default");
            Require(FeelCostPath.CameraModel, "Zoom.Envelope");
            Require(FeelCostPath.CameraModel, "TickEnvelopes");
            Require(FeelCostPath.CameraModel, "LeanOffset");
            Require(FeelCostPath.CameraModel, "ClampLean");
            Require(FeelCostPath.CameraModel, "ApplyKicks");
            Require(FeelCostPath.CameraModel, "Compose");
            Require(FeelCostPath.CameraModel, "SetFocusShift");
            Require(FeelCostPath.CameraModel, "AdvanceTrauma");
            Require(FeelCostPath.Rumble, "Tick");
            Require(FeelCostPath.Rumble, "Row");
            Require(FeelCostPath.Rumble, "Pulse");
            Require(FeelCostPath.Rumble, "OnShotTD");
            Require(FeelCostPath.Rumble, "OnFireStartTD");
            Require(FeelCostPath.Rumble, "OnFireStopTD");
            Require(FeelCostPath.Rumble, "OnHitTD");
            Require(FeelCostPath.Rumble, "OnKillTD");
            Require(FeelCostPath.Rumble, "OnMultiTD");
            Require(FeelCostPath.Rumble, "OnExplosionTD");
            Require(FeelCostPath.Rumble, "OnHurtTD");
            Require(FeelCostPath.Rumble, "OnReloadStartTD");
            Require(FeelCostPath.Rumble, "OnReloadEndTD");
            Require(FeelCostPath.Rumble, "OnDryTD");
            Require(FeelCostPath.Rumble, "OnGoldTD");
            Require(FeelCostPath.Rumble, "OnRoundTD");
            Require(FeelCostPath.Rumble, "OnLastTD");
            Require(FeelCostPath.Rumble, "OnBeatTD");
            Require(FeelCostPath.RumbleMixer, "Event");
            Require(FeelCostPath.RumbleMixer, "Schedule");
            Require(FeelCostPath.RumbleMixer, "SustainOn");
            Require(FeelCostPath.RumbleMixer, "SustainOff");
            Require(FeelCostPath.RumbleMixer, "Sample");
            Require(FeelCostPath.TimeScale, "FramesFor");
            Require(FeelCostPath.TimeScale, "NewAnonymousGroup");
            Require(FeelCostPath.TimeScale, "RequestStop");
            Require(FeelCostPath.TimeScale, "BeginLastZombie");
            Require(FeelCostPath.TimeScale, "LastZombieScaleAt");
            Require(FeelCostPath.TimeScale, "AdvanceLateFrame");
            Require(FeelCostPath.TimeScale, "Advance");
            Require(FeelCostPath.TimeScale, "Refresh");
            Require(FeelCostPath.TimeScale, "Suspend");
            Require(FeelCostPath.HitStop, "OnHit");
            Require(FeelCostPath.HitStop, "OnKill");
            Require(FeelCostPath.HitStop, "OnMultiKill");
            Require(FeelCostPath.HitStop, "OnPlayerDamaged");
            Require(FeelCostPath.HitStop, "OnLastZombie");
            Require(FeelCostPath.HitStop, "OnFireStarted");
            Require(FeelCostPath.HitStop, "OnFireStopped");
            Require(FeelCostPath.HitStop, "NotifyTrigger");
            Require(FeelCostPath.Router, "Enqueue");
            Require(FeelCostPath.Router, "Flush");
            Require(FeelCostPath.Router, "ModeHitStop");
            Require(FeelCostPath.Router, "AdvanceLateFrame");
            Require(FeelCostPath.LegacyHitStop, "Request", FeelCostCallbackClass.CompatibilityControl);
            Require(FeelCostPath.LegacyHitStop, "RequestForGroup");
            Require(FeelCostPath.LegacyHitStop, "VisualDelta");
            Require(FeelCostPath.LegacyHitStop, "Tick");
            Require(FeelCostPath.DeathSequence, "Begin");
            Require(FeelCostPath.DeathSequence, "Tick");
            Require(FeelCostPath.DeathSequence, "ScaleAt");
            Require(FeelCostPath.Muzzle, "TopDownLightScale");
            Require(FeelCostPath.Muzzle, "TopDownLightRange");
            Require(FeelCostPath.Muzzle, "TopDownFlickerFloor");
            Require(FeelCostPath.RunFlow, "AddHold");
            Require(FeelCostPath.RunFlow, "TickFeelRoundEndDeferral", FeelCostCallbackClass.RoundEndProduction);
            Require(FeelCostPath.RunFlow, "ShouldDeferFeelRoundEndHold", FeelCostCallbackClass.RoundEndProduction);
        }

        public static int Calls(FeelCostPath path, string member)
        {
            int slot; return s_members[(int)path].TryGetValue(member, out slot) ? MemberCalls[slot] : 0;
        }

        public static void BeginFrame()
        {
            if (!CpuValid || !GcValid) ErrorBits |= 1;
            if (s_depth != 0) ErrorBits |= 4;
            FrameGcBytes = FrameStopwatchNs = FrameInstrumentationNs = 0;
            FrameGcSamples = FrameRoots = 0;
        }
        public static void ClearWindow()
        {
            Array.Clear(PathCalls, 0, PathCalls.Length); Array.Clear(EventCalls, 0, EventCalls.Length); Array.Clear(MemberCalls, 0, MemberCalls.Length);
            ExternalEvents = CriticalEvents = FireStarts = FireStops = NaturalAsyncEvents = 0; InputHash = 2166136261u;
        }
        public static void Ingress(FeelEventKind kind)
        {
            if (!s_armed || !Sampling) return;
            if (!Replaying)
            {
                bool allowedNatural = NaturalRoundPhase && (kind == FeelEventKind.RoundEnded || kind == FeelEventKind.GoldCollected);
                if (allowedNatural) NaturalAsyncEvents++; else ExternalEvents++;
            }
            int k = (int)kind; EventCalls[k]++; InputHash = (InputHash ^ (uint)(k + 1)) * 16777619u;
        }
        public static void Trigger(bool started)
        {
            if (!s_armed || !Sampling) return;
            if (!Replaying) ExternalEvents++;
            if (started) FireStarts++; else FireStops++;
            InputHash = (InputHash ^ (started ? 16u : 17u)) * 16777619u;
        }
        public static void Critical() { if (Sampling && Replaying) CriticalEvents++; }
        public static void Fault() { if (Sampling) ErrorBits |= 128; }
        public static void Contamination() { if (Sampling) ExternalEvents++; }

        public static Scope Enter(FeelCostPath path, [CallerMemberName] string member = "")
        {
            if (!s_armed || CameraModeService.Current != CameraMode.TopDown || (DiagnosticMixerOnly && !DspSampleCall)) return default;
            bool skip = GateActive && !LayersOn && path != FeelCostPath.Control && path != FeelCostPath.Events;
            if (Thread.CurrentThread.ManagedThreadId != s_thread) { if (Sampling) ErrorBits |= 2; return new Scope(false, skip); }
            long outerStart = Sampling && s_depth == 0 ? Stopwatch.GetTimestamp() : 0;
            var members = s_members[(int)path]; int slot;
            if (!members.TryGetValue(member, out slot))
            {
                if (Sampling) { ErrorBits |= 32; return new Scope(false, skip); }
                if (members.Count >= MembersPerPath) { ErrorBits |= 64; return new Scope(false, skip); }
                slot = (int)path * MembersPerPath + members.Count; members.Add(member, slot); MemberNames[slot] = path + "." + member; CallbackClasses[slot] = FeelCostCallbackClass.WarmObserved;
            }
            if (RegistrationPhase) ExpectedMembers[slot] = true;
            if (!Sampling) return new Scope(false, skip);
            PathCalls[(int)path]++; MemberCalls[slot]++;
            if (s_depth++ == 0)
            {
                s_outerStart = outerStart;
                if (s_gc.Valid) { s_gc.Reset(); s_gc.Start(); }
                s_innerStart = Stopwatch.GetTimestamp(); s_marker.Begin();
            }
            return new Scope(true, skip);
        }
        public readonly struct Scope : IDisposable
        {
            private readonly bool _active;
            public readonly bool Skip;
            internal Scope(bool active, bool skip) { _active = active; Skip = skip; }
            public void Dispose()
            {
                if (!_active) return;
                if (--s_depth != 0) return;
                s_marker.End(); long innerEnd = Stopwatch.GetTimestamp();
                if (s_gc.Valid)
                {
                    s_gc.Stop(); int count = s_gc.Count;
                    if (count >= GcCapacity) ErrorBits |= 8;
                    for (int i = 0; i < count; i++) FrameGcBytes += s_gc.GetSample(i).Value;
                    FrameGcSamples += count;
                }
                FrameRoots++; FrameStopwatchNs += Ns(innerEnd - s_innerStart);
                FrameInstrumentationNs += Ns((Stopwatch.GetTimestamp() - s_outerStart) - (innerEnd - s_innerStart));
            }
        }
        public static long PreviousCpuNs(out long samples)
        {
            samples = 0;
            if (!s_cpu.Valid || s_cpu.Count == 0) { ErrorBits |= 16; return -1; }
            var sample = s_cpu.GetSample(s_cpu.Count - 1); samples = sample.Count;
            return sample.Value;
        }
        public static void Disarm()
        {
            Sampling = GateActive = Replaying = NaturalRoundPhase = DiagnosticMixerOnly = DspSampleCall = false; LayersOn = true;
            if (s_cpu.Valid) s_cpu.Dispose(); if (s_gc.Valid) s_gc.Dispose();
            s_armed = false; s_depth = 0;
        }
    }
}
#endif
