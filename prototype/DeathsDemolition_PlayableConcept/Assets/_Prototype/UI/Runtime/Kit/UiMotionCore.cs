using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>What a tween writes (direction §3). There is deliberately no Graphic.color channel: a colour tween writes CanvasRenderer.SetColor (Tint), never Graphic.color / TMP.color, which would regenerate the mesh every frame.</summary>
    public enum UiChannel : byte
    {
        Alpha = 0,     // CanvasGroup.alpha
        Tint = 1,      // CanvasRenderer.SetColor (multiplies Graphic.color)
        TintAlpha = 2, // CanvasRenderer.SetAlpha
        Position = 3,  // RectTransform.anchoredPosition
        Scale = 4,     // uniform localScale
        RotationZ = 5, // localEulerAngles.z
        Fill = 6,      // Image.fillAmount
        Count = 7,     // integer counter into a TMP_Text via SetText("{0}", v) - 0 allocations
        ShakeX = 8     // horizontal sine around a rest position, ends exactly at rest (DeniedNudge)
    }

    /// <summary>Which clock a tween runs on. Unscaled: menus, HUD, pause, the round-end freeze. Scaled: world-bound things that must stop with the world.</summary>
    public enum UiClock : byte
    {
        Unscaled = 0,
        Scaled = 1
    }

    /// <summary>A generation-checked handle into the tween pool. Default = invalid.</summary>
    public readonly struct UiTweenHandle
    {
        internal readonly int Index;
        internal readonly int Generation;

        internal UiTweenHandle(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid
        {
            get { return Generation > 0; }
        }

        public bool IsAlive
        {
            get { return UiMotion.IsAlive(this); }
        }
    }

    /// <summary>Receives timeline cues without closures (direction §3).</summary>
    public interface IUiMotionListener
    {
        void OnCue(int cueId);
    }

    /// <summary>
    /// WP-068 slice 2 · the single UI motion core (direction §3; animation-specialist P1 +
    /// DSE presets + motion classes). One pool of 256 struct tweens, one LateUpdate
    /// (<see cref="UiMotionDriver"/>), generation handles, no classes / delegates / boxing
    /// per tween: 0 bytes per Play and per frame. The Reduced Motion and flash policy is
    /// applied HERE by <see cref="UiMotionClass"/>, not at call sites:
    ///   Decor     - RM: end state at once (fades 0 ms);
    ///   Delta     - RM: final value at once;
    ///   Data      - RM never changes it;
    ///   Attention - RM: held at the loop's top;
    ///   Flash     - plays; peaks × FlashScale; large onsets through <see cref="FlashOnset"/>.
    /// One tween per (target, channel): a new request retargets from the current value.
    /// Overflow: a tween that finds no free slot lands on its end state (dropping the oldest
    /// would freeze that element mid-way).
    /// </summary>
    public static class UiMotion
    {
        public const int Capacity = 256;
        public const int TimelineCapacity = 32;
        public const int TimelineSteps = 24;
        public const int TimelineCues = 8;

        private struct Tween
        {
            public int Generation;
            public bool Active;
            public UiChannel Channel;
            public UiMotionClass Class;
            public UiEase Ease;
            public UiClock Clock;
            public bool Loop;        // ping-pong forever (Attention pulses)
            public Object Target;    // CanvasGroup / Graphic / RectTransform / Image / TMP_Text
            public Vector4 From;
            public Vector4 To;
            public float Start;
            public float Delay;
            public float Duration;
            public int Owner;
            public int LastCount;
        }

        private struct Cue
        {
            public float Time;
            public UiEvent Event;
            public int Id;
            public bool Fired;
        }

        private struct TimelineData
        {
            public int Generation;
            public bool Active;
            public UiClock Clock;
            public float Start;
            public float Cursor;
            public int Owner;
            public IUiMotionListener Listener;
            public int StepCount;
            public UiTweenHandle[] Steps;
            public int CueCount;
            public Cue[] Cues;
        }

        private static readonly Tween[] s_tweens = new Tween[Capacity];
        private static readonly int[] s_free = new int[Capacity];
        private static int s_freeCount = -1;
        private static int s_active;
        private static int s_generation;

        private static readonly TimelineData[] s_timelines = new TimelineData[TimelineCapacity];
        private static int s_timelinesActive;

        private static readonly float[] s_flashOnsets = new float[UiMotionTokens.FlashGateOnsetsPerSecond];
        private static int s_flashHead;

        /// <summary>Mirrors UiSettings.ReducedMotion (set by UiSettings; tests set it directly).</summary>
        public static bool ReducedMotion;

        /// <summary>Test clock: when set, both clocks read this value (EditMode tests step it and call <see cref="Tick"/>).</summary>
        public static float? TestNow;

        /// <summary>Live tweens (tests / probes).</summary>
        public static int ActiveCount
        {
            get { return s_active; }
        }

        /// <summary>Live timelines (tests / probes).</summary>
        public static int ActiveTimelines
        {
            get { return s_timelinesActive; }
        }

        // ================================================================== public API

        public static UiTweenHandle Alpha(CanvasGroup group, float to, UiDuration duration, UiEase ease, UiMotionClass motionClass, float delay = 0f, UiClock clock = UiClock.Unscaled, int owner = 0)
        {
            if (group == null) return default(UiTweenHandle);
            return Play(group, UiChannel.Alpha, new Vector4(group.alpha, 0f, 0f, 0f), new Vector4(to, 0f, 0f, 0f), Seconds(duration), ease, motionClass, delay, clock, owner, false);
        }

        /// <summary>Colour through CanvasRenderer (multiplies Graphic.color). Use with a white Graphic.color for an absolute colour.</summary>
        public static UiTweenHandle Tint(Graphic graphic, Color to, UiDuration duration, UiEase ease, UiMotionClass motionClass, float delay = 0f, UiClock clock = UiClock.Unscaled, int owner = 0)
        {
            if (graphic == null) return default(UiTweenHandle);
            Color from = graphic.canvasRenderer.GetColor();
            return Play(graphic, UiChannel.Tint, (Vector4)from, (Vector4)to, Seconds(duration), ease, motionClass, delay, clock, owner, false);
        }

        public static UiTweenHandle TintAlpha(Graphic graphic, float to, UiDuration duration, UiEase ease, UiMotionClass motionClass, float delay = 0f, UiClock clock = UiClock.Unscaled, int owner = 0)
        {
            if (graphic == null) return default(UiTweenHandle);
            return Play(graphic, UiChannel.TintAlpha, new Vector4(graphic.canvasRenderer.GetAlpha(), 0f, 0f, 0f), new Vector4(to, 0f, 0f, 0f), Seconds(duration), ease, motionClass, delay, clock, owner, false);
        }

        public static UiTweenHandle Move(RectTransform rect, Vector2 to, UiDuration duration, UiEase ease, UiMotionClass motionClass, float delay = 0f, UiClock clock = UiClock.Unscaled, int owner = 0)
        {
            if (rect == null) return default(UiTweenHandle);
            Vector2 from = rect.anchoredPosition;
            return Play(rect, UiChannel.Position, new Vector4(from.x, from.y, 0f, 0f), new Vector4(to.x, to.y, 0f, 0f), Seconds(duration), ease, motionClass, delay, clock, owner, false);
        }

        public static UiTweenHandle Scale(RectTransform rect, float to, UiDuration duration, UiEase ease, UiMotionClass motionClass, float delay = 0f, UiClock clock = UiClock.Unscaled, int owner = 0)
        {
            if (rect == null) return default(UiTweenHandle);
            return Play(rect, UiChannel.Scale, new Vector4(rect.localScale.x, 0f, 0f, 0f), new Vector4(to, 0f, 0f, 0f), Seconds(duration), ease, motionClass, delay, clock, owner, false);
        }

        public static UiTweenHandle Fill(Image image, float to, UiDuration duration, UiEase ease, UiMotionClass motionClass, float delay = 0f, UiClock clock = UiClock.Unscaled, int owner = 0)
        {
            if (image == null) return default(UiTweenHandle);
            return Play(image, UiChannel.Fill, new Vector4(image.fillAmount, 0f, 0f, 0f), new Vector4(to, 0f, 0f, 0f), Seconds(duration), ease, motionClass, delay, clock, owner, false);
        }

        /// <summary>Integer counter (count-up). Text changes only when the integer changes (TMP SetText, no string).</summary>
        public static UiTweenHandle Count(TMP_Text text, int from, int to, UiDuration duration, UiEase ease, UiMotionClass motionClass, float delay = 0f, UiClock clock = UiClock.Unscaled, int owner = 0)
        {
            if (text == null) return default(UiTweenHandle);
            return Play(text, UiChannel.Count, new Vector4(from, 0f, 0f, 0f), new Vector4(to, 0f, 0f, 0f), Seconds(duration), ease, motionClass, delay, clock, owner, false);
        }

        /// <summary>An Attention pulse on the graphics' CanvasRenderer alpha: min ↔ max, one cycle per period. RM: held at max.</summary>
        public static UiTweenHandle Pulse(Graphic graphic, float min, float max, float period, int owner = 0)
        {
            if (graphic == null) return default(UiTweenHandle);
            return Play(graphic, UiChannel.TintAlpha, new Vector4(max, 0f, 0f, 0f), new Vector4(min, 0f, 0f, 0f), Mathf.Max(0.1f, period * 0.5f), UiEase.Move, UiMotionClass.Attention, 0f, UiClock.Unscaled, owner, true);
        }

        // Token-derived durations that are not one of the six UiDuration steps (the Stamp
        // contact 90 ms, a nudge leg 40 ms) - for UiPresets inside the kit only.
        internal static UiTweenHandle ScaleSeconds(RectTransform rect, float from, float to, float seconds, UiEase ease, UiMotionClass motionClass, float delay = 0f, int owner = 0)
        {
            if (rect == null) return default(UiTweenHandle);
            return Play(rect, UiChannel.Scale, new Vector4(from, 0f, 0f, 0f), new Vector4(to, 0f, 0f, 0f), seconds, ease, motionClass, delay, UiClock.Unscaled, owner, false);
        }

        internal static UiTweenHandle MoveSeconds(RectTransform rect, Vector2 from, Vector2 to, float seconds, UiEase ease, UiMotionClass motionClass, float delay = 0f, int owner = 0)
        {
            if (rect == null) return default(UiTweenHandle);
            return Play(rect, UiChannel.Position, new Vector4(from.x, from.y, 0f, 0f), new Vector4(to.x, to.y, 0f, 0f), seconds, ease, motionClass, delay, UiClock.Unscaled, owner, false);
        }

        internal static UiTweenHandle FillSeconds(Image image, float from, float to, float seconds, UiEase ease, UiMotionClass motionClass, float delay = 0f, int owner = 0)
        {
            if (image == null) return default(UiTweenHandle);
            return Play(image, UiChannel.Fill, new Vector4(from, 0f, 0f, 0f), new Vector4(to, 0f, 0f, 0f), seconds, ease, motionClass, delay, UiClock.Unscaled, owner, false);
        }

        internal static UiTweenHandle ShakeSeconds(RectTransform rect, Vector2 rest, float amplitude, float cycles, float seconds, UiMotionClass motionClass, int owner = 0)
        {
            if (rect == null) return default(UiTweenHandle);
            return Play(rect, UiChannel.ShakeX, new Vector4(rest.x, rest.y, 0f, 0f), new Vector4(amplitude, cycles, 0f, 0f), seconds, UiEase.Linear, motionClass, 0f, UiClock.Unscaled, owner, false);
        }

        public static bool IsAlive(UiTweenHandle handle)
        {
            return handle.IsValid && handle.Index >= 0 && handle.Index < Capacity
                && s_tweens[handle.Index].Active && s_tweens[handle.Index].Generation == handle.Generation;
        }

        /// <summary>Stop a tween; complete = jump to its end state first.</summary>
        public static void Kill(UiTweenHandle handle, bool complete)
        {
            if (!IsAlive(handle)) return;
            if (complete) ApplyEnd(ref s_tweens[handle.Index]);
            Release(handle.Index);
        }

        /// <summary>Stop everything one owner started (UiScreen.OnDestroy passes its instance id).</summary>
        public static void KillOwner(int owner, bool complete)
        {
            if (owner == 0) return;
            for (int i = 0; i < Capacity; i++)
            {
                if (s_tweens[i].Active && s_tweens[i].Owner == owner)
                {
                    if (complete) ApplyEnd(ref s_tweens[i]);
                    Release(i);
                }
            }

            for (int i = 0; i < TimelineCapacity; i++)
            {
                if (s_timelines[i].Active && s_timelines[i].Owner == owner)
                {
                    s_timelines[i].Active = false;
                    s_timelines[i].Listener = null;
                    s_timelinesActive--;
                }
            }
        }

        /// <summary>Stop every tween on a target (any channel).</summary>
        public static void KillTarget(Object target, bool complete)
        {
            if (target == null) return;
            for (int i = 0; i < Capacity; i++)
            {
                if (s_tweens[i].Active && ReferenceEquals(s_tweens[i].Target, target))
                {
                    if (complete) ApplyEnd(ref s_tweens[i]);
                    Release(i);
                }
            }
        }

        /// <summary>
        /// FlashGate (WCAG 2.3.1, direction §2.5): a large flash (area ≥ 43 000 px² at 1080p)
        /// may start at most 3 times per second across the whole UI. Returns false when the
        /// onset must MERGE into the one already running (retarget its peak, do not queue).
        /// Small glints do not count but still multiply by FlashScale.
        /// </summary>
        public static bool FlashOnset(bool large)
        {
            if (!large) return true;
            float now = Now(UiClock.Unscaled);
            int oldest = s_flashHead;
            float oldestTime = s_flashOnsets[oldest];
            if (oldestTime > 0f && now - oldestTime < 1f)
            {
                return false;
            }

            s_flashOnsets[oldest] = now > 0f ? now : 1e-4f;
            s_flashHead = (s_flashHead + 1) % s_flashOnsets.Length;
            return true;
        }

        /// <summary>Advance every tween and timeline. The driver calls this once per frame from LateUpdate; EditMode tests call it with <see cref="TestNow"/> set.</summary>
        public static void Tick()
        {
            if (s_active == 0 && s_timelinesActive == 0) return;

            for (int i = 0; i < Capacity; i++)
            {
                if (!s_tweens[i].Active) continue;
                ref Tween tween = ref s_tweens[i];
                if (tween.Target == null)
                {
                    Release(i);
                    continue;
                }

                float elapsed = Now(tween.Clock) - tween.Start - tween.Delay;
                if (elapsed < 0f) continue;

                if (tween.Loop)
                {
                    float cycle = tween.Duration * 2f;
                    float phase = Mathf.Repeat(elapsed, cycle) / tween.Duration;
                    float k = phase <= 1f ? phase : 2f - phase;
                    Apply(ref tween, UiEaseCurve.Evaluate(tween.Ease, k));
                    continue;
                }

                float t = tween.Duration > 0f ? elapsed / tween.Duration : 1f;
                if (t >= 1f)
                {
                    ApplyEnd(ref tween);
                    Release(i);
                    continue;
                }

                Apply(ref tween, UiEaseCurve.Evaluate(tween.Ease, t));
            }

            TickTimelines();
        }

        // ================================================================== timelines

        /// <summary>Start a timeline. Steps added through it get their delay from its cursor; cues raise UiEvents at their time, in order, within the frame.</summary>
        public static UiTimeline Timeline(int owner, IUiMotionListener listener = null, UiClock clock = UiClock.Unscaled)
        {
            for (int i = 0; i < TimelineCapacity; i++)
            {
                if (s_timelines[i].Active) continue;
                ref TimelineData tl = ref s_timelines[i];
                if (tl.Steps == null)
                {
                    tl.Steps = new UiTweenHandle[TimelineSteps]; // once per pool slot, at first use
                    tl.Cues = new Cue[TimelineCues];
                }

                tl.Generation = ++s_generation;
                tl.Active = true;
                tl.Clock = clock;
                tl.Start = Now(clock);
                tl.Cursor = 0f;
                tl.Owner = owner;
                tl.Listener = listener;
                tl.StepCount = 0;
                tl.CueCount = 0;
                s_timelinesActive++;
                return new UiTimeline(i, tl.Generation);
            }

            return default(UiTimeline);
        }

        internal static bool TimelineAlive(UiTimeline timeline)
        {
            return timeline.IsValid && s_timelines[timeline.Index].Active && s_timelines[timeline.Index].Generation == timeline.Generation;
        }

        internal static float TimelineCursor(UiTimeline timeline)
        {
            return TimelineAlive(timeline) ? s_timelines[timeline.Index].Cursor : 0f;
        }

        internal static void TimelineAt(UiTimeline timeline, float seconds)
        {
            if (TimelineAlive(timeline)) s_timelines[timeline.Index].Cursor = Mathf.Max(0f, seconds);
        }

        internal static void TimelineAdd(UiTimeline timeline, UiTweenHandle handle)
        {
            if (!TimelineAlive(timeline) || !handle.IsValid) return;
            ref TimelineData tl = ref s_timelines[timeline.Index];
            if (tl.StepCount < TimelineSteps)
            {
                tl.Steps[tl.StepCount++] = handle;
            }
        }

        internal static void TimelineCue(UiTimeline timeline, UiEvent uiEvent, int cueId, float seconds)
        {
            if (!TimelineAlive(timeline)) return;
            ref TimelineData tl = ref s_timelines[timeline.Index];
            if (tl.CueCount >= TimelineCues) return;
            // Kept sorted by time so cues fire in authored order within a frame.
            int at = tl.CueCount;
            while (at > 0 && tl.Cues[at - 1].Time > seconds)
            {
                tl.Cues[at] = tl.Cues[at - 1];
                at--;
            }

            tl.Cues[at] = new Cue { Time = seconds, Event = uiEvent, Id = cueId, Fired = false };
            tl.CueCount++;
        }

        /// <summary>Skip: every step lands on its end state and the cues collapse to the LAST one (direction §3); the next press performs the action.</summary>
        internal static void TimelineComplete(UiTimeline timeline)
        {
            if (!TimelineAlive(timeline)) return;
            ref TimelineData tl = ref s_timelines[timeline.Index];
            for (int i = 0; i < tl.StepCount; i++)
            {
                Kill(tl.Steps[i], true);
            }

            int last = -1;
            for (int i = 0; i < tl.CueCount; i++)
            {
                if (!tl.Cues[i].Fired) last = i;
            }

            if (last >= 0)
            {
                FireCue(ref tl, last);
            }

            EndTimeline(timeline.Index);
        }

        internal static float TimelineNow(UiTimeline timeline)
        {
            return TimelineAlive(timeline) ? Now(s_timelines[timeline.Index].Clock) - s_timelines[timeline.Index].Start : 0f;
        }

        private static void TickTimelines()
        {
            if (s_timelinesActive == 0) return;
            for (int i = 0; i < TimelineCapacity; i++)
            {
                if (!s_timelines[i].Active) continue;
                ref TimelineData tl = ref s_timelines[i];
                float t = Now(tl.Clock) - tl.Start;
                bool pending = false;
                for (int c = 0; c < tl.CueCount; c++)
                {
                    if (tl.Cues[c].Fired) continue;
                    if (tl.Cues[c].Time <= t)
                    {
                        FireCue(ref tl, c);
                    }
                    else
                    {
                        pending = true;
                    }
                }

                bool running = false;
                for (int s = 0; s < tl.StepCount && !running; s++)
                {
                    running = IsAlive(tl.Steps[s]);
                }

                if (!pending && !running)
                {
                    EndTimeline(i);
                }
            }
        }

        private static void FireCue(ref TimelineData tl, int index)
        {
            tl.Cues[index].Fired = true;
            UiEvents.Raise(tl.Cues[index].Event);
            if (tl.Listener != null)
            {
                tl.Listener.OnCue(tl.Cues[index].Id);
            }
        }

        private static void EndTimeline(int index)
        {
            if (!s_timelines[index].Active) return;
            s_timelines[index].Active = false;
            s_timelines[index].Listener = null;
            s_timelinesActive--;
        }

        // ================================================================== internals

        private static float Seconds(UiDuration duration)
        {
            return UiMotionTokens.Seconds(duration);
        }

        internal static float Now(UiClock clock)
        {
            if (TestNow.HasValue) return TestNow.Value;
            return clock == UiClock.Scaled ? Time.time : Time.unscaledTime;
        }

        private static UiTweenHandle Play(Object target, UiChannel channel, Vector4 from, Vector4 to, float duration, UiEase ease, UiMotionClass motionClass, float delay, UiClock clock, int owner, bool loop)
        {
            EnsureFreeList();
            UiMotionDriver.Ensure();

            // Policy by class (direction §2.5 table).
            if (ReducedMotion)
            {
                switch (motionClass)
                {
                    case UiMotionClass.Decor:
                    case UiMotionClass.Delta:
                        StopExisting(target, channel);
                        var landed = new Tween { Target = target, Channel = channel, From = from, To = to, LastCount = int.MinValue };
                        ApplyEnd(ref landed);
                        return default(UiTweenHandle);
                    case UiMotionClass.Attention:
                        StopExisting(target, channel);
                        var held = new Tween { Target = target, Channel = channel, From = from, To = loop ? from : to, LastCount = int.MinValue };
                        ApplyEnd(ref held);
                        return default(UiTweenHandle);
                }
            }

            // One tween per (target, channel): retarget from the current value.
            int slot = Find(target, channel);
            if (slot < 0)
            {
                if (s_freeCount == 0)
                {
                    // Overflow: land on the end state rather than evicting a running tween.
                    var overflow = new Tween { Target = target, Channel = channel, From = from, To = to, LastCount = int.MinValue };
                    ApplyEnd(ref overflow);
                    return default(UiTweenHandle);
                }

                slot = s_free[--s_freeCount];
                s_active++;
            }

            ref Tween tween = ref s_tweens[slot];
            int lastCount = tween.Active && tween.Channel == UiChannel.Count ? tween.LastCount : int.MinValue;
            tween.Generation = ++s_generation;
            tween.Active = true;
            tween.Target = target;
            tween.Channel = channel;
            tween.Class = motionClass;
            tween.Ease = ease;
            tween.Clock = clock;
            tween.Loop = loop;
            tween.From = from;
            tween.To = to;
            tween.Start = Now(clock);
            tween.Delay = Mathf.Max(0f, delay);
            tween.Duration = Mathf.Max(0f, duration);
            tween.Owner = owner;
            tween.LastCount = lastCount;

            if (tween.Duration <= 0f && tween.Delay <= 0f && !loop)
            {
                ApplyEnd(ref tween);
                Release(slot);
                return default(UiTweenHandle);
            }

            return new UiTweenHandle(slot, tween.Generation);
        }

        private static void StopExisting(Object target, UiChannel channel)
        {
            int slot = Find(target, channel);
            if (slot >= 0) Release(slot);
        }

        private static int Find(Object target, UiChannel channel)
        {
            if (s_active == 0) return -1;
            for (int i = 0; i < Capacity; i++)
            {
                if (s_tweens[i].Active && s_tweens[i].Channel == channel && ReferenceEquals(s_tweens[i].Target, target))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void EnsureFreeList()
        {
            if (s_freeCount >= 0) return;
            for (int i = 0; i < Capacity; i++)
            {
                s_free[i] = Capacity - 1 - i;
            }

            s_freeCount = Capacity;
        }

        private static void Release(int index)
        {
            if (!s_tweens[index].Active) return;
            s_tweens[index].Active = false;
            s_tweens[index].Target = null;
            s_free[s_freeCount++] = index;
            s_active--;
        }

        private static void ApplyEnd(ref Tween tween)
        {
            Apply(ref tween, 1f, true);
        }

        private static void Apply(ref Tween tween, float k)
        {
            Apply(ref tween, k, false);
        }

        private static void Apply(ref Tween tween, float k, bool end)
        {
            Vector4 v = end ? tween.To : Vector4.LerpUnclamped(tween.From, tween.To, k);
            switch (tween.Channel)
            {
                case UiChannel.Alpha:
                    var group = tween.Target as CanvasGroup;
                    if (group != null) group.alpha = v.x;
                    break;
                case UiChannel.Tint:
                    var tinted = tween.Target as Graphic;
                    if (tinted != null) tinted.canvasRenderer.SetColor(new Color(v.x, v.y, v.z, v.w));
                    break;
                case UiChannel.TintAlpha:
                    var faded = tween.Target as Graphic;
                    if (faded != null) faded.canvasRenderer.SetAlpha(v.x);
                    break;
                case UiChannel.Position:
                    var moved = tween.Target as RectTransform;
                    if (moved != null) moved.anchoredPosition = new Vector2(v.x, v.y);
                    break;
                case UiChannel.Scale:
                    var scaled = tween.Target as RectTransform;
                    if (scaled != null) scaled.localScale = new Vector3(v.x, v.x, 1f);
                    break;
                case UiChannel.RotationZ:
                    var rotated = tween.Target as RectTransform;
                    if (rotated != null) rotated.localRotation = Quaternion.Euler(0f, 0f, v.x);
                    break;
                case UiChannel.Fill:
                    var image = tween.Target as Image;
                    if (image != null) image.fillAmount = v.x;
                    break;
                case UiChannel.ShakeX:
                    var shaken = tween.Target as RectTransform;
                    if (shaken != null)
                    {
                        float offset = end ? 0f : Mathf.Sin(k * tween.To.y * 2f * Mathf.PI) * tween.To.x * (1f - k);
                        shaken.anchoredPosition = new Vector2(tween.From.x + offset, tween.From.y);
                    }

                    break;
                case UiChannel.Count:
                    var text = tween.Target as TMP_Text;
                    int value = Mathf.RoundToInt(v.x);
                    if (text != null && value != tween.LastCount)
                    {
                        tween.LastCount = value;
                        text.SetText("{0}", value);
                    }

                    break;
            }
        }

        /// <summary>Test seam: drop every tween and timeline, reset the flash gate.</summary>
        public static void ResetForTests()
        {
            for (int i = 0; i < Capacity; i++)
            {
                s_tweens[i].Active = false;
                s_tweens[i].Target = null;
            }

            for (int i = 0; i < TimelineCapacity; i++)
            {
                s_timelines[i].Active = false;
                s_timelines[i].Listener = null;
            }

            for (int i = 0; i < s_flashOnsets.Length; i++)
            {
                s_flashOnsets[i] = 0f;
            }

            s_freeCount = -1;
            s_active = 0;
            s_timelinesActive = 0;
            s_flashHead = 0;
            EnsureFreeList();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResetForTests();
            TestNow = null;
        }
    }

    /// <summary>A generation-checked timeline builder (struct, no allocation).</summary>
    public readonly struct UiTimeline
    {
        internal readonly int Index;
        internal readonly int Generation;

        internal UiTimeline(int index, int generation)
        {
            Index = index;
            Generation = generation;
        }

        public bool IsValid
        {
            get { return Generation > 0; }
        }

        public bool IsAlive
        {
            get { return UiMotion.TimelineAlive(this); }
        }

        /// <summary>Seconds since the timeline started.</summary>
        public float Elapsed
        {
            get { return UiMotion.TimelineNow(this); }
        }

        /// <summary>Set the offset for the steps that follow.</summary>
        public UiTimeline At(float seconds)
        {
            UiMotion.TimelineAt(this, seconds);
            return this;
        }

        /// <summary>The current step offset (for building a delay into a Play call).</summary>
        public float Cursor
        {
            get { return UiMotion.TimelineCursor(this); }
        }

        /// <summary>Attach a tween started with delay = <see cref="Cursor"/> so Complete() can land it.</summary>
        public UiTimeline Add(UiTweenHandle handle)
        {
            UiMotion.TimelineAdd(this, handle);
            return this;
        }

        /// <summary>Raise <paramref name="uiEvent"/> at <paramref name="seconds"/> (in order within the frame); the listener gets <paramref name="cueId"/>.</summary>
        public UiTimeline Cue(UiEvent uiEvent, float seconds, int cueId = 0)
        {
            UiMotion.TimelineCue(this, uiEvent, cueId, seconds);
            return this;
        }

        /// <summary>Skip to the end: visuals at t = 1, cues collapse to the last one.</summary>
        public void Complete()
        {
            UiMotion.TimelineComplete(this);
        }
    }

    /// <summary>
    /// The one per-frame entry point (direction §3): LateUpdate after the screens' Update,
    /// before Canvas.willRenderCanvases. Created on first use, survives scene loads, and
    /// idles when nothing is animating.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public sealed class UiMotionDriver : MonoBehaviour
    {
        private static UiMotionDriver s_instance;

        internal static void Ensure()
        {
            if (s_instance != null || !Application.isPlaying) return;
            var go = new GameObject("UiMotionDriver") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<UiMotionDriver>();
        }

        private void LateUpdate()
        {
            UiMotion.Tick();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
        }
    }
}
