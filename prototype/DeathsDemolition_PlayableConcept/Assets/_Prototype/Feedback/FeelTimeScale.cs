using DD.Prototype.CameraSystem;
using DD.Prototype.Rounds;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// WP-095 K1 / K2 / K5 (C-00 08.10; his sözleşmesi §4.11, §5, §9 S3 / S4) · the ONE owner of <c>Time.timeScale</c> under the
    /// pause clock. REQ-127 changed: in top-down a global hit-stop slows the world now. The three things that want a slower
    /// clock no longer write it themselves; each holds a request slot here and the arbiter writes min(slots):
    /// <list type="bullet">
    /// <item>the global hit-stop (top-down; <see cref="FeelHitStopTD"/> asks from <see cref="FeelEvents"/>);</item>
    /// <item>the last zombie's slow motion (top-down; <see cref="BeginLastZombie"/>);</item>
    /// <item>the player's death beat (<see cref="DeathSequence"/>, every mode — it keeps its own envelope and asks through
    /// <see cref="Refresh"/>).</item>
    /// </list>
    /// Rules:
    /// <list type="bullet">
    /// <item><b>The pause wins.</b> While <see cref="RunFlowClock.IsFrozen"/> nothing ages and nothing is written. A top-down pause
    /// SUSPENDS the envelopes (<see cref="Suspend"/>: the pause clock gives back the base, 1 — never the 0.06 / 0.25 / 0.5 on
    /// screen) and the next frame re-applies what is left. First / third person, the round-end and the results holds END them,
    /// as before K1 (mode-agnostic clock code keeps its FP / TP behaviour).</item>
    /// <item><b>Foreign writers win.</b> The arbiter only writes a clock that still shows its own last value. A capture agent
    /// (UiCaptureAgent, UiFxCaptureAgent, QaReadabilityGate, FirstPersonArmsCaptureAgent) writing <c>Time.timeScale</c> ends every
    /// envelope; the arbiter stays quiet until a new request finds a running clock. A clock stopped by someone else (≤ 0.01)
    /// refuses every request.</item>
    /// <item><b>No accumulation (K2).</b> One global stop at a time, ≤ <see cref="MaxStopFrames"/> frames (0.067 s); another group
    /// is refused while it runs; the same group may escalate up to the cap; no new stop within <c>globalStopMinInterval</c>
    /// (0.40 s) of the last one's end.</item>
    /// <item><b>Death wins.</b> The death beat drops the top-down slots and refuses new ones (§4.11: P 100).</item>
    /// </list>
    /// Time base: real (unscaled) seconds, a frame = 1/60 s; the arbiter's <see cref="Clock"/> stands still while frozen. A request
    /// made during a frame keeps its whole first frame: <see cref="Advance"/> ages old slots before it promotes new ones. Advanced
    /// once a frame from <see cref="FeelEventRouter.AdvanceLateFrame"/> (HitFeedbackDirector.LateUpdate, order -100) after the
    /// flush; the hidden <see cref="FeelTimeScaleRunner"/> (order 32000) is the fallback in a scene without the director. 0 B per
    /// frame and per request.
    /// </summary>
    public static class FeelTimeScale
    {
        /// <summary>His sözleşmesi: a "frame" is 1/60 s of real time; at another frame rate the seconds count.</summary>
        public const float FrameSeconds = 1f / 60f;

        /// <summary>K2 (C-00 08.10): the longest global stop, 4 frames = 0.067 s (the profile's maxStopFrames is clamped to it).</summary>
        public const int MaxStopFrames = 4;

        /// <summary>K2 (C-00 08.10): no global stop starts within this many real seconds of the last one's end (the profile may say more).</summary>
        public const float MinStopIntervalSeconds = 0.40f;

        /// <summary>K5 (C-00 08.10): the round-end freeze waits for the last zombie at most this long (the profile may say less).</summary>
        public const float MaxRoundEndDeferSeconds = 1f;

        private const float Eps = 1e-4f;

        // ------------------------------------------------------------------ state

        private static float s_clock;
        private static int s_lastAdvanceFrame = -1;

        // the global hit-stop slot (top-down)
        private static bool s_stopActive;
        private static bool s_stopFresh;
        private static int s_stopGroup;
        private static FeelEventId s_stopKind;
        private static float s_stopTotal;
        private static float s_stopElapsed;
        private static float s_stopScale = 0.06f;
        private static float s_lastStopEnd = float.NegativeInfinity;
        private static int s_anonymousGroup;

        // the last-zombie slot (top-down)
        private static bool s_lzActive;
        private static bool s_lzFresh;
        private static float s_lzElapsed;
        private static float s_lzIn;
        private static float s_lzHold;
        private static float s_lzOut;
        private static float s_lzScale = 1f;
        private static Vector3 s_lzPosition;

        // clock ownership
        private static bool s_driving;
        private static float s_base = 1f;
        private static float s_written = 1f;

        // ------------------------------------------------------------------ readouts (camera, RunDirector, tests, F1, telemetry)

        /// <summary>The arbiter's real-time clock (seconds); it does not move while <see cref="RunFlowClock.IsFrozen"/>.</summary>
        public static float Clock => s_clock;

        /// <summary>A top-down global stop was granted (this frame's request included).</summary>
        public static bool GlobalStopActive => s_stopActive;

        /// <summary>The global stop is on the clock now (granted and past its request frame) — the world runs at the stop scale.</summary>
        public static bool GlobalStopApplied => s_stopActive && !s_stopFresh;

        public static int ActiveStopGroup => s_stopActive ? s_stopGroup : 0;
        public static FeelEventId ActiveStopKind => s_stopKind;

        /// <summary>Length of the running stop in frames (escalation included), 0 when none.</summary>
        public static int ActiveStopFrames => s_stopActive ? Mathf.RoundToInt(s_stopTotal / FrameSeconds) : 0;

        public static float StopRemainingSeconds => s_stopActive ? Mathf.Max(0f, s_stopTotal - s_stopElapsed) : 0f;

        /// <summary>The last zombie's slow motion runs (request frame included). RunDirector defers the round-end freeze on it (K5).</summary>
        public static bool LastZombieActive => s_lzActive;

        /// <summary>Real seconds since the slow motion began (0 when idle).</summary>
        public static float LastZombieElapsed => s_lzActive ? s_lzElapsed : 0f;

        /// <summary>In + hold + out of the running (or last) slow motion, real seconds.</summary>
        public static float LastZombieTotalSeconds => s_lzIn + s_lzHold + s_lzOut;

        /// <summary>0 → 1 along the slow motion (linear in real time); 0 when idle.</summary>
        public static float LastZombieProgress01
        {
            get
            {
                float total = LastZombieTotalSeconds;
                return s_lzActive && total > 0f ? Mathf.Clamp01(s_lzElapsed / total) : 0f;
            }
        }

        /// <summary>
        /// 0 → 1 → 0, the slow motion's depth (1 = at lastZombieTimeScale), the same easing as the clock: in easeOutCubic, hold,
        /// out easeInOutSine. The camera's focus shift and zoom (K5 −8 %) ride this, so picture and clock move together.
        /// </summary>
        public static float LastZombieBlend01 => s_lzActive && s_lzScale < 1f ? Mathf.Clamp01((1f - LastZombieScaleAt(s_lzElapsed)) / (1f - s_lzScale)) : 0f;

        /// <summary>Where the last zombie died (the focus point of the moment).</summary>
        public static Vector3 LastZombiePosition => s_lzPosition;

        /// <summary>The arbiter is holding the clock (it wrote a scale and owes the base back).</summary>
        public static bool IsDriving => s_driving;

        /// <summary>A slot is open or the arbiter still owes the clock (tests' leak guard).</summary>
        public static bool HasWork => s_stopActive || s_lzActive || s_driving;

        /// <summary>The scale a requester returns to: the arbiter's base while it drives, else the live one (1 when stopped).</summary>
        public static float BaseScale => s_driving ? s_base : (Time.timeScale > 0.01f ? Time.timeScale : 1f);

        /// <summary>K5: top-down, the last zombie's slow motion is running — RunDirector takes the round-end hold when it ends.</summary>
        public static bool ShouldDeferRoundEndHold => s_lzActive && IsTopDown;

        /// <summary>K5: the deferral's ceiling, the profile's roundEndHoldSafetySeconds clamped to <see cref="MaxRoundEndDeferSeconds"/>.</summary>
        public static float RoundEndHoldSafetySeconds => Mathf.Clamp(FeelProfileTD.Active.roundEndHoldSafetySeconds, 0f, MaxRoundEndDeferSeconds);

        public static int Stops { get; private set; }
        public static int Extended { get; private set; }
        public static int Refused { get; private set; }
        public static int Guarded { get; private set; }
        public static int LastZombies { get; private set; }
        public static int Yields { get; private set; }
        public static int Suspends { get; private set; }

        /// <summary>Real seconds the world ran at the global stop scale (telemetry `feel.hitstop`: the share of play time, ≤ 6 %).</summary>
        public static float StoppedSeconds { get; private set; }

        private static bool IsTopDown => CameraModeService.Current == CameraMode.TopDown;

        // ------------------------------------------------------------------ requests

        /// <summary>
        /// His sözleşmesi §5: a §4.0 row's frames × the weapon's hitStopScale → nearest frame (half up), kept in [1, maxFrames];
        /// a row without a stop (0) stays without one. A missing weapon row (scale 0) counts as 1.
        /// </summary>
        public static int FramesFor(int baseFrames, float hitStopScale, int maxFrames)
        {
            if (baseFrames <= 0) return 0;
            float scale = hitStopScale > 0f ? hitStopScale : 1f;
            int cap = Mathf.Clamp(maxFrames, 1, MaxStopFrames);
            return Mathf.Clamp(Mathf.FloorToInt(baseFrames * scale + 0.5f), 1, cap);
        }

        /// <summary>A group of its own for an ungrouped moment (player damage, an ungrouped kill): negative, never a trigger pull's.</summary>
        public static int NewAnonymousGroup()
        {
            s_anonymousGroup--;
            if (s_anonymousGroup >= 0) s_anonymousGroup = -1;
            return s_anonymousGroup;
        }

        /// <summary>
        /// Top-down global hit-stop for <paramref name="frames"/> real frames (already × HS scale) at the profile's
        /// globalStopTimeScale. True when the stop runs (started, escalated, or won its frame). Refused: not top-down, the
        /// hitStopGlobal switch off, another group's stop on the clock, inside the 0.40 s interval, the pause / death beat / a
        /// foreign clock. The same group escalates to the longer request, never past maxStopFrames (K2); inside the frame the
        /// stop was granted, a longer request of another group takes it over (one stop per frame, the strongest).
        /// </summary>
        public static bool RequestStop(int groupId, int frames, FeelEventId kind)
        {
            if (frames <= 0 || !IsTopDown) return false;
            FeelProfileTD profile = FeelProfileTD.Active;
            if (!profile.hitStopGlobal) return false;
            if (groupId == 0) groupId = NewAnonymousGroup();

            int cap = Mathf.Clamp(profile.maxStopFrames, 1, MaxStopFrames);
            float seconds = Mathf.Min(frames, cap) * FrameSeconds;
            if (s_stopActive)
            {
                if (groupId == s_stopGroup)
                {
                    if (seconds > s_stopTotal + Eps)
                    {
                        s_stopTotal = seconds;   // the same pull escalates (armour break → kill → multi-kill), capped
                        s_stopKind = kind;
                        Extended++;
                    }

                    return true;
                }

                if (s_stopFresh && seconds > s_stopTotal + Eps)
                {
                    // Granted this frame, not on the clock yet: the longest of the frame's requests takes the one stop, whatever
                    // the order the events came in (the router's one-winner-per-flush rule). The weaker one counts as refused.
                    s_stopGroup = groupId;
                    s_stopKind = kind;
                    s_stopTotal = seconds;
                    Refused++;
                    return true;
                }

                Refused++;   // running: another group neither restarts nor extends it
                return false;
            }

            if (RunFlowClock.IsFrozen || DeathSequence.IsActive || !ClockAvailable())
            {
                Refused++;
                return false;
            }

            if (s_clock - s_lastStopEnd < Mathf.Max(MinStopIntervalSeconds, profile.globalStopMinInterval) - Eps)
            {
                Refused++;   // K2: ≥ 0.40 s of real play between two global stops (the profile may ask for more)
                return false;
            }

            s_stopActive = true;
            s_stopFresh = true;
            s_stopGroup = groupId;
            s_stopKind = kind;
            s_stopTotal = seconds;
            s_stopElapsed = 0f;
            s_stopScale = Mathf.Clamp(profile.globalStopTimeScale, 0.01f, 1f);
            Stops++;
            FeelTimeScaleRunner.Ensure();
            return true;
        }

        /// <summary>
        /// §4.11: the last zombie's slow motion — 1 → lastZombieTimeScale over lastZombieInSeconds (easeOutCubic), held
        /// lastZombieHoldSeconds, back to 1 over lastZombieOutSeconds (easeInOutSine); real seconds, ≈ 0.83 s. The 4-frame stop
        /// that opens the moment is a separate <see cref="RequestStop"/>: both start together and the clock shows the smaller,
        /// so the in-ramp runs under the stop and the stop hands over to the slow motion without a lurch back to 1.
        /// Refused: not top-down, the lastZombieSlowMo switch off, one already running, the pause / death beat / a foreign clock.
        /// </summary>
        public static bool BeginLastZombie(Vector3 position)
        {
            if (!IsTopDown) return false;
            FeelProfileTD profile = FeelProfileTD.Active;
            if (!profile.lastZombieSlowMo) return false;
            if (s_lzActive || RunFlowClock.IsFrozen || DeathSequence.IsActive || !ClockAvailable())
            {
                Refused++;
                return false;
            }

            s_lzIn = Mathf.Max(0f, profile.lastZombieInSeconds);
            s_lzHold = Mathf.Max(0f, profile.lastZombieHoldSeconds);
            s_lzOut = Mathf.Max(0f, profile.lastZombieOutSeconds);
            s_lzScale = Mathf.Clamp(profile.lastZombieTimeScale, 0.05f, 1f);
            s_lzElapsed = 0f;
            s_lzPosition = position;
            s_lzActive = true;
            s_lzFresh = true;
            LastZombies++;
            FeelTimeScaleRunner.Ensure();
            return true;
        }

        /// <summary>The slow motion's scale at <paramref name="t"/> real seconds (from 1, the arbiter takes min with its base).</summary>
        public static float LastZombieScaleAt(float t)
        {
            if (t < s_lzIn)
            {
                float u = s_lzIn > 0f ? t / s_lzIn : 1f;
                return Mathf.LerpUnclamped(1f, s_lzScale, EaseOutCubic(u));
            }

            if (t < s_lzIn + s_lzHold)
            {
                return s_lzScale;
            }

            float o = s_lzOut > 0f ? Mathf.Clamp01((t - s_lzIn - s_lzHold) / s_lzOut) : 1f;
            return Mathf.LerpUnclamped(s_lzScale, 1f, EaseInOutSine(o));
        }

        /// <summary>§5 auto-fire guard bookkeeping (FeelHitStopTD): a stop the guard swallowed.</summary>
        internal static void CountGuarded() => Guarded++;

        // ------------------------------------------------------------------ the frame

        /// <summary>Production: once a frame (the router's late frame, else the runner) — a second call in the same frame is a no-op.</summary>
        public static void AdvanceLateFrame(float unscaledDeltaTime)
        {
            int frame = Time.frameCount;
            if (frame == s_lastAdvanceFrame) return;
            s_lastAdvanceFrame = frame;
            Advance(unscaledDeltaTime);
        }

        /// <summary>
        /// Age the slots by <paramref name="unscaledDeltaTime"/> real seconds, then write the clock. Nothing moves while the run
        /// clock is frozen. A slot granted since the last call keeps its first frame (aged from the next call on). Tests drive
        /// this directly.
        /// </summary>
        public static void Advance(float unscaledDeltaTime)
        {
            if (RunFlowClock.IsFrozen) return;   // the pause owns the clock; the envelopes wait where they are
            if ((s_stopActive || s_lzActive) && !IsTopDown) ClearTopDownSlots();   // the mode left top-down mid-moment

            float dt = unscaledDeltaTime > 0f ? unscaledDeltaTime : 0f;
            s_clock += dt;

            if (s_stopActive)
            {
                if (s_stopFresh)
                {
                    s_stopFresh = false;
                }
                else
                {
                    s_stopElapsed += dt;
                    StoppedSeconds += dt;
                    if (s_stopElapsed >= s_stopTotal - Eps) EndStop();
                }
            }

            if (s_lzActive)
            {
                if (s_lzFresh)
                {
                    s_lzFresh = false;
                }
                else
                {
                    s_lzElapsed += dt;
                    if (s_lzElapsed >= LastZombieTotalSeconds - Eps)
                    {
                        s_lzActive = false;
                        s_lzElapsed = 0f;
                    }
                }
            }

            Refresh();
        }

        /// <summary>
        /// Write min(slots) now (a requester changed: the death beat ticked / began / ended). With no slot left the base goes
        /// back — even inside RunFlowClock.AddHold, before it writes 0, so the pause remembers the base. The pause and foreign
        /// writers still win: frozen → nothing written; a clock that no longer shows the arbiter's value → every envelope ends.
        /// </summary>
        public static void Refresh()
        {
            bool death = DeathSequence.IsActive;
            if (!s_stopActive && !s_lzActive && !death)
            {
                GiveBack();
                return;
            }

            if (RunFlowClock.IsFrozen) return;

            float now = Time.timeScale;
            if (!s_driving)
            {
                if (now <= 0.01f)
                {
                    Yield();   // somebody else stopped the clock
                    return;
                }

                s_driving = true;
                s_base = now;
                s_written = now;
                // a step this arbiter scaled and nobody restored is never the new base (f2b: 0.02 → 0.004 → 0.001 compounding)
                if (!OurScaledStep()) s_baseFixed = FixedStep;
            }
            else if (Mathf.Abs(now - s_written) > Eps)
            {
                Yield();   // a foreign writer took the clock: the moment is over, the scale is theirs
                return;
            }

            float target = s_base;
            if (s_stopActive) target = Mathf.Min(target, s_stopScale);
            if (s_lzActive) target = Mathf.Min(target, LastZombieScaleAt(s_lzElapsed));
            if (death) target = Mathf.Min(target, DeathSequence.CurrentScale);
            if (now != target) Time.timeScale = target;
            s_written = Time.timeScale;
            SyncFixed(target);
        }

        // ------------------------------------------------------------------ the pause clock and the endings

        /// <summary>
        /// RunFlowClock.AddHold, just before it freezes. Top-down and a pause-like hold (pause, debug): the envelopes are kept,
        /// the clock is handed back at the base and that base is returned — the pause restores it, the next frame re-applies
        /// what is left (K1). First / third person, the round-end and the results holds: everything ends now and the clock is
        /// handed back (pre-K1 behaviour); returns -1 = "remember the live scale". The local VFX envelope (HitStopService)
        /// ends at any freeze, as before.
        /// </summary>
        public static float Suspend(string holdId)
        {
            HitStopService.Cancel();
            bool pauseLike = holdId != RunFlowClock.RoundEndHold && holdId != RunFlowClock.ResultsHold;
            bool any = s_stopActive || s_lzActive || DeathSequence.IsActive;
            if (pauseLike && any && IsTopDown)
            {
                if (!s_driving)
                {
                    Suspends++;   // granted this frame, nothing written yet: kept, applied after the release
                    return -1f;
                }

                if (Mathf.Abs(Time.timeScale - s_written) <= Eps)
                {
                    Suspends++;
                    s_written = s_base;   // the release restores the base; Refresh sees its own value and goes on
                    RestoreFixed();
                    return s_base;
                }
            }

            Cancel();
            return -1f;
        }

        /// <summary>Every envelope ends now (the top-down slots and the death beat); the clock goes back if it is still ours.</summary>
        public static void Cancel()
        {
            ClearTopDownSlots();
            if (DeathSequence.IsActive) DeathSequence.Cancel();   // → Refresh → GiveBack
            GiveBack();
        }

        /// <summary>The death beat began: the top-down slots end without a write of their own (the beat writes next, §4.11 P 100).</summary>
        internal static void NotifyDeathBegan() => ClearTopDownSlots();

        /// <summary>A scene change ends the top-down moment (it belongs to the scene); the death beat keeps its pre-K1 behaviour.</summary>
        internal static void OnSceneChanged()
        {
            if (!s_stopActive && !s_lzActive) return;
            ClearTopDownSlots();
            Refresh();
        }

        private static bool ClockAvailable() => s_driving ? Mathf.Abs(Time.timeScale - s_written) <= Eps : Time.timeScale > 0.01f;

        private static void GiveBack()
        {
            if (!s_driving) return;
            s_driving = false;
            if (Mathf.Abs(Time.timeScale - s_written) <= Eps) Time.timeScale = s_base > 0.01f ? s_base : 1f;
            RestoreFixed();
        }

        // S-02 (08.10, F2 report risk "physics"): at the last zombie's 0.25 a fixed step of 0.02 scaled seconds runs every 0.08 s
        // real — corpses, debris and gold stutter. While the arbiter slows the clock the physics step shrinks with it (never
        // below 0.2 × base: a 0.06 hit-stop keeps ~1 step per 4 frames, not 16 per frame); the base comes back on every exit.
        private const float MinFixedRatio = 0.2f;
        private static float s_baseFixed = -1f;
        private static float s_writtenFixed = -1f;   // the scaled step this arbiter wrote last (-1: none outstanding)

        private static bool OurScaledStep() => s_writtenFixed > 0f && s_baseFixed > 0f && Mathf.Abs(FixedStep - s_writtenFixed) < 1e-7f;

        /// <summary>
        /// Test seam: EditMode tests point the step here. Outside play mode Time.fixedDeltaTime IS the project's TimeManager setting
        /// (f2b: the arbiter's EditMode tests saved "Fixed Timestep: 0.001" into ProjectSettings) — it is never written there.
        /// </summary>
        internal static bool TestFixedSink;
        internal static float TestFixedValue = 0.02f;

        private static float FixedStep
        {
            get => TestFixedSink ? TestFixedValue : Time.fixedDeltaTime;
            set
            {
                if (TestFixedSink) TestFixedValue = value;
                else if (Application.isPlaying) Time.fixedDeltaTime = value;
            }
        }

        private static void SyncFixed(float target)
        {
            if (s_baseFixed <= 0f) return;
            if (!IsTopDown)
            {
                RestoreFixed();   // gt-f2 MUST-1: first / third person keep today's step (the death beat drives the clock there too)
                return;
            }

            float ratio = s_base > 0.01f ? Mathf.Clamp(target / s_base, MinFixedRatio, 1f) : 1f;
            if (ratio >= 1f)
            {
                RestoreFixed();
                return;
            }

            if (!TestFixedSink && !Application.isPlaying) return;   // never the project's TimeManager
            float fixedDt = s_baseFixed * ratio;
            if (Mathf.Abs(FixedStep - fixedDt) > 1e-7f) FixedStep = fixedDt;
            s_writtenFixed = fixedDt;
        }

        private static void RestoreFixed()
        {
            if (s_baseFixed <= 0f) return;
            // only our own scaled step goes back; a step somebody else set since then is theirs
            if (s_writtenFixed > 0f && Mathf.Abs(FixedStep - s_writtenFixed) < 1e-7f) FixedStep = s_baseFixed;
            s_writtenFixed = -1f;
        }

        private static void Yield()
        {
            Yields++;
            ClearTopDownSlots();
            DeathSequence.EndQuietly();   // the beat is over, the scale is the foreign writer's (pre-K1 DeathSequence rule)
            s_driving = false;
            RestoreFixed();               // the clock is theirs, the physics step is ours to give back
        }

        private static void EndStop()
        {
            s_stopActive = false;
            s_stopFresh = false;
            s_stopElapsed = 0f;
            s_stopGroup = 0;
            s_lastStopEnd = s_clock;
        }

        private static void ClearTopDownSlots()
        {
            if (s_stopActive) EndStop();
            s_lzActive = false;
            s_lzFresh = false;
            s_lzElapsed = 0f;
        }

        private static float EaseOutCubic(float x)
        {
            float u = 1f - Mathf.Clamp01(x);
            return 1f - u * u * u;
        }

        private static float EaseInOutSine(float x) => -(Mathf.Cos(Mathf.PI * Mathf.Clamp01(x)) - 1f) * 0.5f;

        // ------------------------------------------------------------------ lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            FeelHitStopTD.Rearm();
            FeelTimeScaleRunner.Ensure();
        }

        /// <summary>Tests (EditMode and PlayMode leak guard): every slot, counter and the ownership cleared; the clock is not written.</summary>
        public static void ResetForTests()
        {
            RestoreFixed();   // S-02 (f2a): a test that ended mid-slow-motion must not leave the physics step scaled for the next one
            ResetStatics();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_clock = 0f;
            s_lastAdvanceFrame = -1;
            s_stopActive = false;
            s_stopFresh = false;
            s_stopGroup = 0;
            s_stopKind = default;
            s_stopTotal = 0f;
            s_stopElapsed = 0f;
            s_stopScale = 0.06f;
            s_lastStopEnd = float.NegativeInfinity;
            s_anonymousGroup = 0;
            s_lzActive = false;
            s_lzFresh = false;
            s_lzElapsed = 0f;
            s_lzIn = s_lzHold = s_lzOut = 0f;
            s_lzScale = 1f;
            s_lzPosition = Vector3.zero;
            s_driving = false;
            s_base = 1f;
            s_written = 1f;
            // s_baseFixed / s_writtenFixed survive a statics reset on purpose: a scaled step left behind is still recognised and given back
            Stops = Extended = Refused = Guarded = LastZombies = Yields = Suspends = 0;
            StoppedSeconds = 0f;
        }
    }

    /// <summary>
    /// WP-095 K1 · the arbiter's fallback tick (a scene without HitFeedbackDirector) and its scene hook (the top-down moment ends
    /// with its scene, the FeelEvents subscriber re-arms). One hidden persistent object; runs after every other LateUpdate, so in
    /// a scene with the director its call is the frame's second and does nothing.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class FeelTimeScaleRunner : MonoBehaviour
    {
        private static FeelTimeScaleRunner s_instance;

        internal static void Ensure()
        {
            if (s_instance != null || !Application.isPlaying)
            {
                return;   // EditMode tests drive FeelTimeScale.Advance themselves
            }

            var go = new GameObject("[Feel Time Scale]");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<FeelTimeScaleRunner>();
        }

        private void OnEnable() => SceneManager.activeSceneChanged += OnActiveSceneChanged;

        private void OnDisable() => SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        private static void OnActiveSceneChanged(Scene from, Scene to)
        {
            FeelTimeScale.OnSceneChanged();
            FeelHitStopTD.Rearm();
        }

        private void LateUpdate() => FeelTimeScale.AdvanceLateFrame(Time.unscaledDeltaTime);

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }
    }
}
