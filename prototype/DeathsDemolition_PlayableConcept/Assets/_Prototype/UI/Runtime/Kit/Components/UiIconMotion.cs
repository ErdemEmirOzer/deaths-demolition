using DD.Prototype.Core;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>WP-098A (DEC-114; HAREKET-MENU §3) · the one-shot tracks an icon control plays, highest priority last but one.</summary>
    public enum UiIconTrack : byte
    {
        None = 0,
        /// <summary>lifted 0 → 1 (hover = focus): 180 ms SettleLong to the lifted pose; the sheen passes 0 → 1.</summary>
        Lift = 1,
        /// <summary>an exit event: 120 ms Sweep from the pose on screen to the composite rest; the sheen jumps at t0.</summary>
        Settle = 2,
        /// <summary>DEC-114: the one focused icon's single 3.6 s sheen pass, 2.4 s after focus, only while the scene is calm.</summary>
        Idle = 3,
        /// <summary>SetNotification / PlayNotify: 600 ms Sweep, a sheen pass and a 1.05 nudge (the transform done by 400 ms).</summary>
        Notify = 4,
        /// <summary>a user's choice (chosen 0 → 1): 420 ms; the stroke front 0.75 → 1, the transform done by 260 ms.</summary>
        Choose = 5,
        /// <summary>pointer down / Submit (PressPulse): 120 ms Sweep, the contact (0.94, +0.5 px24, the lob down to Elev0) at 50 ms.</summary>
        Press = 6,
        /// <summary>interactable 1 → 0: 160 ms Sweep; the light goes flat at t0 (UiIconButton), no sheen, no glow.</summary>
        Disable = 7
    }

    /// <summary>
    /// WP-098A · an icon's pose: the glyph's scale and vertical offset (AP-028 px24 units, + down, × the glyph's px24 size on
    /// screen), and the Icon material's light channels - sheen (−1 off | 0..1 the band's travel), glow (0..0.08), the stroke
    /// front (draw 0..1) and the lift (0..1, the lob Elev1 → Elev2).
    /// </summary>
    public struct UiIconPose
    {
        public float Scale;
        public float OffsetY;
        public float Sheen;
        public float Glow;
        public float Draw;
        public float Lift;

        /// <summary>No track: identity, the sheen off, no glow, drawn, at Elev1 (also the Reduced Motion and disabled rest).</summary>
        public static UiIconPose Rest
        {
            get { return new UiIconPose { Scale = 1f, OffsetY = 0f, Sheen = -1f, Glow = 0f, Draw = 1f, Lift = 0f }; }
        }

        /// <summary>hover / focus (AP-028 hover_focus's last key): 1.025, −0.25 px24 (up), sheen 1, glow 0.04, the lob at Elev2.</summary>
        public static UiIconPose Lifted
        {
            get { return new UiIconPose { Scale = 1.025f, OffsetY = -0.25f, Sheen = 1f, Glow = 0.04f, Draw = 1f, Lift = 1f }; }
        }

        /// <summary>chosen and not lifted (selected's last key): identity, sheen 1, glow 0.04.</summary>
        public static UiIconPose Chosen
        {
            get { return new UiIconPose { Scale = 1f, OffsetY = 0f, Sheen = 1f, Glow = 0.04f, Draw = 1f, Lift = 0f }; }
        }

        public static UiIconPose Lerp(UiIconPose a, UiIconPose b, float u)
        {
            return new UiIconPose
            {
                Scale = a.Scale + (b.Scale - a.Scale) * u,
                OffsetY = a.OffsetY + (b.OffsetY - a.OffsetY) * u,
                Sheen = a.Sheen + (b.Sheen - a.Sheen) * u,
                Glow = a.Glow + (b.Glow - a.Glow) * u,
                Draw = a.Draw + (b.Draw - a.Draw) * u,
                Lift = a.Lift + (b.Lift - a.Lift) * u
            };
        }

        /// <summary>Every channel exactly equal (field by field: no boxing, unlike ValueType.Equals).</summary>
        public bool Same(UiIconPose other)
        {
            return Scale == other.Scale && OffsetY == other.OffsetY && Sheen == other.Sheen && Glow == other.Glow && Draw == other.Draw && Lift == other.Lift;
        }

        /// <summary>Every channel within <paramref name="epsilon"/> (a write below it is invisible and skipped).</summary>
        public bool Near(UiIconPose other, float epsilon)
        {
            return Mathf.Abs(Scale - other.Scale) < epsilon && Mathf.Abs(OffsetY - other.OffsetY) < epsilon && Mathf.Abs(Sheen - other.Sheen) < epsilon
                   && Mathf.Abs(Glow - other.Glow) < epsilon * 0.1f && Mathf.Abs(Draw - other.Draw) < epsilon && Mathf.Abs(Lift - other.Lift) < epsilon;
        }
    }

    /// <summary>
    /// WP-098A (C-00 08.10; DEC-114; HAREKET-MENU §3 and M7 / M9 / M18; ikon v2 council interaction-motion §1–§3) · the icon state
    /// tracks and the idle arbiter. One fixed pool of <see cref="Capacity"/> struct runs ticked from <see cref="UiMotion.Tick"/>
    /// (no Update anywhere - T6), one run per icon; channels are written only while a run plays and only when they change
    /// (≥ 1/512): at rest nothing is written. A track starts from the pose on screen and ends on the icon's COMPOSITE rest,
    /// read live each tick (Reduced Motion / disabled → <see cref="UiIconPose.Rest"/>, lifted → <see cref="UiIconPose.Lifted"/>,
    /// chosen → <see cref="UiIconPose.Chosen"/>, else Rest), so a hover change during a press or a choice only changes where it
    /// lands. Priority: Disable &gt; Press (its 50 ms contact protected: a choice waits for it) &gt; Choose &gt; Notify &gt;
    /// Lift / Settle &gt; Idle. At most <see cref="UiMenuMotionTokens.MaxHoverTraces"/> hover traces at once (fast travel lands
    /// the oldest). No track starts on the frame an icon is bound, behind the curtain or under Reduced Motion (the rest at once;
    /// a running track lands on the next tick when RM turns on).
    /// Idle (DEC-114): the lifted icon is the one candidate; <see cref="UiMotionTokens.IconIdleDelay"/> after focus arrives, with
    /// the scene calm - RM off, the window focused, the curtain open, no other icon track, no hero choreography
    /// (<see cref="HeroActive"/>), the icon inside <see cref="IdleScope"/> when a screen sets one (a pane open: the rail only), the
    /// icon material on (not the Performance tier) - it plays its one 3.6 s pass; at most one idle run exists, once per focus
    /// episode (focus loss, a press, a choice or a disable ends the episode). Suppressed when due, it re-arms the delay; dropped
    /// mid-pass, the episode is not spent. Reduce Flashes: the shader scales the sheen and the glow by FlashScale.
    ///
    /// USAGE: UiIconButton calls <see cref="Play"/> / <see cref="Focused"/> / <see cref="Unfocused"/> from its button's state events;
    /// a screen sets <see cref="HeroActive"/> around a hero choreography and <see cref="IdleScope"/> while a pane is open.
    /// DON'T: tween an icon's glyph or its Icon channels yourself (one writer), loop a sheen on the GPU, or start a track on a
    /// screen's bind frame.
    /// ACCESSIBILITY: Reduced Motion lands every track at once (the kit's brackets and wash carry the state; no idle pass);
    /// Reduce Flashes scales the light in the shader; no track carries information the static state does not.
    /// </summary>
    public static class UiIconMotion
    {
        public const int Capacity = 32;

        private struct Run
        {
            public bool Active;
            public UiIconButton Icon;
            public UiIconTrack Track;
            public float Start;
            public float Duration;
            public UiIconPose From;
        }

        private static readonly Run[] s_runs = new Run[Capacity];
        private static int s_active;

        private static UiIconButton s_candidate;
        private static float s_idleDue;
        private static bool s_idleSpent;

        /// <summary>A hero choreography runs (the menu's arrival, a screen transition): no idle pass, no ink wave (HAREKET-MENU §6).</summary>
        public static bool HeroActive;

        /// <summary>When set, only icons under it may idle (the menu sets the rail while a pane is open; DEC-114 "odak ray dışında").</summary>
        public static RectTransform IdleScope;

        /// <summary>Tests: the window's focus as the arbiter sees it (the batch Editor never has focus); null = the real one.</summary>
        public static bool? FocusForTests;

        /// <summary>Runs playing now (tests).</summary>
        public static int ActiveRuns
        {
            get { return s_active; }
        }

        /// <summary>Idle runs playing now (tests: never more than one).</summary>
        public static int IdleRuns
        {
            get { return CountOf(UiIconTrack.Idle); }
        }

        /// <summary>The track <paramref name="icon"/> plays now (None at rest).</summary>
        public static UiIconTrack TrackOf(UiIconButton icon)
        {
            int slot = SlotOf(icon);
            return slot >= 0 ? s_runs[slot].Track : UiIconTrack.None;
        }

        /// <summary>The idle candidate (the lifted icon), null when none.</summary>
        public static UiIconButton IdleCandidate
        {
            get { return s_candidate; }
        }

        public static int CountOf(UiIconTrack track)
        {
            int n = 0;
            for (int i = 0; i < Capacity; i++)
            {
                if (s_runs[i].Active && s_runs[i].Track == track) n++;
            }

            return n;
        }

        public static float DurationOf(UiIconTrack track)
        {
            switch (track)
            {
                case UiIconTrack.Lift: return UiMotionTokens.IconLift;
                case UiIconTrack.Settle: return UiMotionTokens.IconSettle;
                case UiIconTrack.Idle: return UiMotionTokens.IconIdle;
                case UiIconTrack.Notify: return UiMotionTokens.IconNotify;
                case UiIconTrack.Choose: return UiMotionTokens.IconChoose;
                case UiIconTrack.Press: return UiMotionTokens.IconPress;
                case UiIconTrack.Disable: return UiMotionTokens.IconDisable;
                default: return 0f;
            }
        }

        // ================================================================== starting

        /// <summary>
        /// Play <paramref name="track"/> on <paramref name="icon"/> from the pose on screen (priority and the hover-trace cap apply).
        /// Under RM, behind the curtain, on its bind frame or inactive, the icon lands on its composite rest at once.
        /// </summary>
        public static void Play(UiIconButton icon, UiIconTrack track)
        {
            if (icon == null || track == UiIconTrack.None)
            {
                return;
            }

            float now = UiMotion.Now(UiClock.Unscaled);
            if (track != UiIconTrack.Lift && track != UiIconTrack.Idle && icon == s_candidate)
            {
                // A press, a choice, a disable, a settle off focus: the focus episode is over (no idle pass after it).
                EndEpisode(icon);
            }

            if (UiMotion.ReducedMotion || UiCurtain.IsCovering || icon.BoundAt(now) || !icon.gameObject.activeInHierarchy)
            {
                Stop(icon, true);
                return;
            }

            int slot = SlotOf(icon);
            if (slot >= 0)
            {
                UiIconTrack running = s_runs[slot].Track;
                float elapsed = now - s_runs[slot].Start;
                if (track == UiIconTrack.Choose && running == UiIconTrack.Press && elapsed < UiMotionTokens.IconPressContact)
                {
                    icon.PendingChoose = true;   // the press's contact first (a pad Submit presses and chooses in one frame)
                    return;
                }

                if (Priority(running) > Priority(track) && track != UiIconTrack.Disable)
                {
                    return;   // a lift / settle only moves where the running track lands (it reads the rest live)
                }
            }

            if ((track == UiIconTrack.Lift || track == UiIconTrack.Settle) && slot < 0)
            {
                CapHoverTraces();
            }

            if (slot < 0)
            {
                slot = FreeSlot();
                if (slot < 0)
                {
                    icon.ApplyPose(icon.RestPose(), true);   // overflow: land, never evict a running track
                    return;
                }

                s_active++;
            }

            UiIconPose from = icon.ShownPose;
            UiIconPose rest = icon.RestPose();
            switch (track)
            {
                case UiIconTrack.Lift:
                case UiIconTrack.Idle:
                case UiIconTrack.Notify:
                    from.Sheen = 0f;   // the band enters at the top-left and passes once
                    break;
                case UiIconTrack.Settle:
                case UiIconTrack.Disable:
                    from.Sheen = rest.Sheen;   // never swept back across the glyph
                    break;
                case UiIconTrack.Choose:
                    from.Draw = 0.75f;   // the brush front re-inks the glyph
                    from.Sheen = 0f;
                    break;
            }

            icon.PendingChoose = false;
            s_runs[slot] = new Run { Active = true, Icon = icon, Track = track, Start = now, Duration = DurationOf(track), From = from };
            icon.ApplyPose(Evaluate(track, 0f, from, rest));
            UiMotionDriver.Ensure();
        }

        /// <summary>Focus arrived on <paramref name="icon"/>: it becomes the idle candidate, due <see cref="UiMotionTokens.IconIdleDelay"/> from now.</summary>
        public static void Focused(UiIconButton icon)
        {
            if (icon == null)
            {
                return;
            }

            if (s_candidate != null && s_candidate != icon)
            {
                DropIdle(s_candidate);
            }

            s_candidate = icon;
            s_idleSpent = false;
            s_idleDue = UiMotion.Now(UiClock.Unscaled) + UiMotionTokens.IconIdleDelay;
            UiMotionDriver.Ensure();
        }

        /// <summary>Focus left <paramref name="icon"/>: its episode ends (a running idle pass lands - the sheen jumps, never swept back).</summary>
        public static void Unfocused(UiIconButton icon)
        {
            if (icon != null && icon == s_candidate)
            {
                EndEpisode(icon);
            }
        }

        /// <summary>Stop <paramref name="icon"/>'s run (a destroyed or disabled control); <paramref name="land"/> writes its composite rest.</summary>
        public static void Stop(UiIconButton icon, bool land)
        {
            int slot = SlotOf(icon);
            if (slot >= 0)
            {
                Free(slot);
            }

            if (land && icon != null)
            {
                icon.ApplyPose(icon.RestPose(), true);
            }
        }

        /// <summary>The icon is going away: no run, no candidate refers to it.</summary>
        public static void Forget(UiIconButton icon)
        {
            Stop(icon, false);
            if (icon == s_candidate)
            {
                s_candidate = null;
            }
        }

        // ================================================================== the tick

        /// <summary>One frame: every run steps (or lands), then the idle arbiter. Called by <see cref="UiMotion.Tick"/>; 0 B.</summary>
        public static void Tick(float now)
        {
            if (s_active == 0 && s_candidate == null)
            {
                return;
            }

            if (UiMotion.ReducedMotion)
            {
                LandAll();
                return;
            }

            for (int i = 0; i < Capacity; i++)
            {
                if (!s_runs[i].Active)
                {
                    continue;
                }

                UiIconButton icon = s_runs[i].Icon;
                if (icon == null || !icon.gameObject.activeInHierarchy)
                {
                    Free(i);
                    continue;
                }

                float t = now - s_runs[i].Start;
                if (s_runs[i].Track == UiIconTrack.Idle && IdleSuppressed(icon, true))
                {
                    Free(i);
                    icon.ApplyPose(icon.RestPose(), true);   // dropped mid-pass: not spent; the delay re-arms
                    s_idleDue = now + UiMotionTokens.IconIdleDelay;
                    continue;
                }

                if (s_runs[i].Track == UiIconTrack.Press && icon.PendingChoose && t >= UiMotionTokens.IconPressContact)
                {
                    // The contact landed: the choice plays from here.
                    icon.PendingChoose = false;
                    UiIconPose from = icon.ShownPose;
                    from.Draw = 0.75f;
                    from.Sheen = 0f;
                    s_runs[i].Track = UiIconTrack.Choose;
                    s_runs[i].Start = now;
                    s_runs[i].Duration = UiMotionTokens.IconChoose;
                    s_runs[i].From = from;
                    t = 0f;
                }

                UiIconPose rest = icon.RestPose();
                if (t >= s_runs[i].Duration)
                {
                    bool idle = s_runs[i].Track == UiIconTrack.Idle;
                    Free(i);
                    icon.ApplyPose(rest, true);   // the end is the rest itself, exact
                    if (idle && icon == s_candidate)
                    {
                        s_idleSpent = true;   // one pass per focus episode
                    }

                    continue;
                }

                icon.ApplyPose(Evaluate(s_runs[i].Track, t, s_runs[i].From, rest));
            }

            TickIdle(now);
        }

        private static void TickIdle(float now)
        {
            UiIconButton icon = s_candidate;
            if (icon == null)
            {
                return;
            }

            if (!icon.gameObject.activeInHierarchy || !icon.Lifted)
            {
                EndEpisode(icon);
                return;
            }

            if (s_idleSpent || now < s_idleDue || SlotOf(icon) >= 0)
            {
                return;
            }

            if (IdleSuppressed(icon, false))
            {
                s_idleDue = now + UiMotionTokens.IconIdleDelay;   // calm first, then the delay again
                return;
            }

            Play(icon, UiIconTrack.Idle);
        }

        /// <summary>DEC-114's suppressions (a running idle pass counts itself out of "another icon track").</summary>
        private static bool IdleSuppressed(UiIconButton icon, bool running)
        {
            if (UiMotion.ReducedMotion || HeroActive || UiCurtain.IsCovering || !(FocusForTests ?? Application.isFocused))
            {
                return true;
            }

            if (Time.timeScale < 0.5f)
            {
                return true;   // DEC-114 (run and HUD scope): a run's pause and its round-end shop hold the world - no idle pass there
            }

            if (IdleScope != null && !icon.transform.IsChildOf(IdleScope))
            {
                return true;
            }

            if ((UiFxGlobals.IconTierOverride ?? QualityTiers.Pick(0f, 1f, 1f)) < 0.5f)
            {
                return true;   // Performance: the sheen is off - 3.6 s of invisible writes
            }

            return s_active - (running ? 1 : 0) > 0;
        }

        // ================================================================== evaluation (pure)

        /// <summary>
        /// The pose of <paramref name="track"/> at <paramref name="t"/> s from <paramref name="from"/> toward <paramref name="rest"/>.
        /// Pure and allocation-free (the EditMode tests read it directly).
        /// </summary>
        public static UiIconPose Evaluate(UiIconTrack track, float t, UiIconPose from, UiIconPose rest)
        {
            switch (track)
            {
                case UiIconTrack.Lift:
                    return UiIconPose.Lerp(from, rest, Ease(UiEase.SettleLong, t, UiMotionTokens.IconLift));

                case UiIconTrack.Settle:
                case UiIconTrack.Disable:
                {
                    UiIconPose pose = UiIconPose.Lerp(from, rest, Ease(UiEase.Sweep, t, DurationOf(track)));
                    pose.Sheen = rest.Sheen;
                    return pose;
                }

                case UiIconTrack.Press:
                {
                    UiIconPose contact = from;
                    contact.Scale = 0.94f;
                    contact.OffsetY = 0.5f;
                    contact.Lift = 0f;   // the lob drops to Elev0 under the press
                    if (t < UiMotionTokens.IconPressContact)
                    {
                        return UiIconPose.Lerp(from, contact, Ease(UiEase.Sweep, t, UiMotionTokens.IconPressContact));
                    }

                    return UiIconPose.Lerp(contact, rest, Ease(UiEase.Sweep, t - UiMotionTokens.IconPressContact, UiMotionTokens.IconPress - UiMotionTokens.IconPressContact));
                }

                case UiIconTrack.Choose:
                {
                    UiIconPose pose = UiIconPose.Lerp(from, rest, Ease(UiEase.SettleLong, t, UiMotionTokens.IconChooseTransform));
                    float light = Ease(UiEase.SettleLong, t, UiMotionTokens.IconChoose);
                    pose.Draw = from.Draw + (rest.Draw - from.Draw) * light;
                    pose.Sheen = from.Sheen + (Mathf.Max(rest.Sheen, 1f) - from.Sheen) * light;
                    pose.Glow = from.Glow + (rest.Glow - from.Glow) * light;
                    return pose;
                }

                case UiIconTrack.Notify:
                {
                    // The transform inside Emphasis (a 1.05 nudge and back by 400 ms), the light to 600.
                    UiIconPose pose = rest;
                    float nudge = t < 0.15f ? Ease(UiEase.Sweep, t, 0.15f) : 1f - Ease(UiEase.Sweep, t - 0.15f, 0.25f);
                    pose.Scale = rest.Scale + (1.05f - rest.Scale) * nudge;
                    pose.Sheen = from.Sheen + (1f - from.Sheen) * Ease(UiEase.Sweep, t, UiMotionTokens.IconNotify);
                    pose.Glow = Mathf.Max(rest.Glow, 0.04f * nudge);
                    return pose;
                }

                case UiIconTrack.Idle:
                {
                    UiIconPose pose = rest;
                    pose.Sheen = from.Sheen + (1f - from.Sheen) * Ease(UiEase.Sweep, t, UiMotionTokens.IconIdle);
                    return pose;
                }

                default:
                    return rest;
            }
        }

        private static float Ease(UiEase ease, float t, float duration)
        {
            return UiEaseCurve.Evaluate(ease, duration > 0f ? t / duration : 1f);
        }

        private static int Priority(UiIconTrack track)
        {
            switch (track)
            {
                case UiIconTrack.Disable: return 6;
                case UiIconTrack.Press: return 5;
                case UiIconTrack.Choose: return 4;
                case UiIconTrack.Notify: return 3;
                case UiIconTrack.Lift:
                case UiIconTrack.Settle: return 2;
                case UiIconTrack.Idle: return 1;
                default: return 0;
            }
        }

        // ================================================================== pool

        private static int SlotOf(UiIconButton icon)
        {
            if (icon == null || s_active == 0)
            {
                return -1;
            }

            for (int i = 0; i < Capacity; i++)
            {
                if (s_runs[i].Active && ReferenceEquals(s_runs[i].Icon, icon))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int FreeSlot()
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (!s_runs[i].Active)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void Free(int slot)
        {
            if (!s_runs[slot].Active)
            {
                return;
            }

            s_runs[slot].Active = false;
            s_runs[slot].Icon = null;
            s_active--;
        }

        /// <summary>Fast travel: the oldest hover / settle trace lands so at most MaxHoverTraces − 1 remain before a new one starts.</summary>
        private static void CapHoverTraces()
        {
            while (true)
            {
                int count = 0, oldest = -1;
                float oldestStart = float.MaxValue;
                for (int i = 0; i < Capacity; i++)
                {
                    if (!s_runs[i].Active || (s_runs[i].Track != UiIconTrack.Lift && s_runs[i].Track != UiIconTrack.Settle))
                    {
                        continue;
                    }

                    count++;
                    if (s_runs[i].Start < oldestStart)
                    {
                        oldestStart = s_runs[i].Start;
                        oldest = i;
                    }
                }

                if (count < UiMenuMotionTokens.MaxHoverTraces || oldest < 0)
                {
                    return;
                }

                UiIconButton icon = s_runs[oldest].Icon;
                Free(oldest);
                if (icon != null)
                {
                    icon.ApplyPose(icon.RestPose(), true);
                }
            }
        }

        private static void DropIdle(UiIconButton icon)
        {
            int slot = SlotOf(icon);
            if (slot >= 0 && s_runs[slot].Track == UiIconTrack.Idle)
            {
                Free(slot);
                icon.ApplyPose(icon.RestPose(), true);
            }
        }

        private static void EndEpisode(UiIconButton icon)
        {
            DropIdle(icon);
            if (icon == s_candidate)
            {
                s_candidate = null;
            }
        }

        private static void LandAll()
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (!s_runs[i].Active)
                {
                    continue;
                }

                UiIconButton icon = s_runs[i].Icon;
                Free(i);
                if (icon != null)
                {
                    icon.ApplyPose(icon.RestPose(), true);
                }
            }

            s_candidate = null;
        }

        /// <summary>Test seam: no run, no candidate, the flags back to their defaults.</summary>
        public static void ResetForTests()
        {
            for (int i = 0; i < Capacity; i++)
            {
                s_runs[i] = default(Run);
            }

            s_active = 0;
            s_candidate = null;
            s_idleSpent = false;
            s_idleDue = 0f;
            HeroActive = false;
            IdleScope = null;
            FocusForTests = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResetForTests();
        }
    }
}
