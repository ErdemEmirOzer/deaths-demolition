using System;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-094 HUD v4 · the gold target (R3, C-00 07.10; S-09 → S-04, contract K/wp-094/ALTIN-HEDEF-API.md): where a coin
    /// flying out of the world lands on the HUD, and the ledger that holds the counter back while it flies.
    ///
    /// THE POINT
    /// The gold symbol's centre - the coin glyph directly above the health stroke - in Screen / Input pixels (origin at the
    /// bottom left), read from the bound HUD's flight target: an unscaled rect on the coin's own rect, so the arrival punch
    /// never moves it. RectTransformUtility with the HUD canvas's camera (null for the overlay the HUD is). False when there
    /// is no HUD (menus, loading, Results), on the frame the HUD was built, while it is hidden (a shop / Pause panel holds
    /// the frozen frame, the player is down) or its canvas is off.
    /// <see cref="LayoutVersion"/> moves whenever the point or the symbol's size on screen does (resolution, UI scale, the
    /// 16:9 frame, the camera-mode layout): the bound HUD compares once a frame in <see cref="TrackLayout"/> - one centre and
    /// one size read, no allocation.
    ///
    /// THE LEDGER
    /// <see cref="Depart"/>(n) puts n in flight and the HUD shows balance − <see cref="InFlight"/>, so the counter waits for
    /// the coin. <see cref="Arrive"/>(n) releases the oldest flights first (FIFO, a partial amount allowed); the counter
    /// counts up and the symbol punches - the HUD plays at most one punch per <see cref="PunchMinSeconds"/>, the "+N"
    /// accumulating meanwhile - then <see cref="Arrived"/> rises with n: once per call, so a round-end sweep of 40 coins
    /// raises it 40 times (the sound side keeps its own limiter).
    /// - Order: a Depart in the frame of its wallet credit may come before or after the credit (HudGoldReadout settles the
    ///   frame); a Depart frames before its credit holds the counter where it stood - it never counts DOWN for a flight.
    /// - Never stuck: a flight older than <see cref="StaleSeconds"/> (unscaled) releases itself (the counter counts up, no
    ///   punch, no event); the ledger empties when a HUD binds (its build), at run start and run end, and in
    ///   <see cref="ResetStatics"/>.
    /// - Arrive with nothing in flight only punches and glints. Depart / Arrive with n ≤ 0 do nothing.
    /// - <see cref="FlightCapacity"/> flights are tracked one by one; past that a new one merges into the newest (the total
    ///   stays exact, the merged share takes the newer clock).
    /// - Reduced motion: the caller skips the flight and calls Arrive at once; the HUD turns the punch into the glint alone.
    /// Without a Depart the HUD is the HUD it was before WP-094: the counter takes the balance on the frame the credit lands.
    ///
    /// Unscaled time throughout (an Arrive under Pause is handled as any other). Main thread only. No call allocates; the
    /// ring is allocated once.
    /// STATIC BUS: <see cref="ResetStatics"/> (SubsystemRegistration, and tests) drops every <see cref="Arrived"/> listener -
    /// a process-long listener re-subscribes on every scene change (the static-bus rearm lesson, AudioEventRouter.Rearm).
    /// </summary>
    public static class HudGoldTarget
    {
        /// <summary>A flight that has not arrived after this long (unscaled seconds) is released by itself.</summary>
        public const float StaleSeconds = 2f;

        /// <summary>The HUD's punch rate limit: at most one punch per this many seconds, the gain accumulating between (the motion token).</summary>
        public const float PunchMinSeconds = UiMotionTokens.PunchMinInterval;

        /// <summary>Flights tracked one by one (a round-end sweep is ≤ one per packet; the pool is sized per round).</summary>
        public const int FlightCapacity = 256;

        /// <summary>The point moves this far (screen px) before <see cref="LayoutVersion"/> does: sub-pixel drift is not a layout.</summary>
        public const float LayoutTolerance = 0.5f;

        private static readonly int[] s_amount = new int[FlightCapacity];
        private static readonly float[] s_departAt = new float[FlightCapacity];
        private static int s_head;
        private static int s_count;

        private static IHudGoldView s_view;
        private static RectTransform s_target;
        private static Canvas s_canvas;
        private static int s_boundFrame = -1;
        private static Vector2 s_layoutCentre = new Vector2(float.NaN, float.NaN);
        private static Vector2 s_layoutSize;

        /// <summary>
        /// After <see cref="Arrive"/>, once the HUD has started its reaction (S-08 sound, S-07 VFX). The argument is this
        /// arrival's amount. Raised once per Arrive call; a listener that throws is logged, never thrown into the caller.
        /// </summary>
        public static event Action<int> Arrived;

        /// <summary>Moves whenever the symbol moves on screen (resolution, UI scale, layout): read the point again when it changes.</summary>
        public static int LayoutVersion { get; private set; }

        /// <summary>Gold departed and not yet arrived (tests; the HUD shows balance minus this).</summary>
        public static int InFlight { get; private set; }

        /// <summary>
        /// The gold symbol's centre in screen pixels (Screen / Input space, origin bottom left). False: no HUD (menu, loading,
        /// Results), the HUD is hidden (a panel over it, the player down) or it was built this frame - valid from the next.
        /// </summary>
        public static bool TryGetScreenPoint(out Vector2 screenPoint)
        {
            if (!Ready())
            {
                screenPoint = default(Vector2);
                return false;
            }

            screenPoint = CentreOnScreen();
            return true;
        }

        /// <summary>The same point on the plane <paramref name="distance"/> metres in front of <paramref name="camera"/> (for a flight in world space).</summary>
        public static bool TryGetWorldPoint(Camera camera, float distance, out Vector3 worldPoint)
        {
            Vector2 screen;
            if (camera == null || !TryGetScreenPoint(out screen))
            {
                worldPoint = default(Vector3);
                return false;
            }

            worldPoint = camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, distance));
            return true;
        }

        /// <summary>A coin left the world for the HUD: the counter stays <paramref name="amount"/> behind until it arrives.</summary>
        public static void Depart(int amount)
        {
            Depart(amount, Time.unscaledTime);
        }

        /// <summary>A coin reached the symbol: <paramref name="amount"/> is released, the counter counts up, the symbol punches and glints.</summary>
        public static void Arrive(int amount)
        {
            Arrive(amount, Time.unscaledTime);
        }

        // ================================================================== ledger (explicit clock: the tests' seam)

        internal static void Depart(int amount, float now)
        {
            if (amount <= 0)
            {
                return;
            }

            ReleaseStale(now);
            Push(amount, now);
            InFlight += amount;
            NotifyFlights();
        }

        internal static void Arrive(int amount, float now)
        {
            if (amount <= 0)
            {
                return;
            }

            ReleaseStale(now);
            int left = amount;
            while (left > 0 && s_count > 0)
            {
                int take = Mathf.Min(left, s_amount[s_head]);
                s_amount[s_head] -= take;
                left -= take;
                InFlight -= take;
                if (s_amount[s_head] <= 0)
                {
                    PopHead();
                }
            }

            if (ViewLive())
            {
                s_view.GoldArrived(amount);
            }

            Action<int> handler = Arrived;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(amount);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>Releases every flight older than <see cref="StaleSeconds"/>; returns the amount released. The bound HUD calls it once a frame.</summary>
        internal static int Tick(float now)
        {
            int released = ReleaseStale(now);
            if (released > 0)
            {
                NotifyFlights();
            }

            return released;
        }

        /// <summary>Empties the ledger (run start, run end); the HUD re-reads its counter.</summary>
        internal static void ResetFlights()
        {
            bool had = InFlight != 0 || s_count != 0;
            ClearFlights();
            if (had)
            {
                NotifyFlights();
            }
        }

        // ================================================================== the HUD's side

        /// <summary>A HUD holds the target (its build bound it and it is still alive).</summary>
        internal static bool IsBound
        {
            get { return s_view != null && s_target != null; }
        }

        internal static bool IsBoundTo(IHudGoldView view)
        {
            return view != null && ReferenceEquals(s_view, view) && s_target != null;
        }

        /// <summary>The HUD's build: its flight target rect and its canvas. A (re)build empties the ledger.</summary>
        internal static void Bind(IHudGoldView view, RectTransform target, Canvas canvas)
        {
            s_view = view;
            s_target = target;
            s_canvas = canvas != null ? canvas.rootCanvas : null;
            s_boundFrame = Time.frameCount;
            s_layoutCentre = new Vector2(float.NaN, float.NaN);
            LayoutVersion++;
            ClearFlights();
        }

        /// <summary>The HUD is going: nothing to fly to, nothing to hold.</summary>
        internal static void Unbind(IHudGoldView view)
        {
            if (!ReferenceEquals(s_view, view))
            {
                return;
            }

            s_view = null;
            s_target = null;
            s_canvas = null;
            s_boundFrame = -1;
            s_layoutCentre = new Vector2(float.NaN, float.NaN);
            LayoutVersion++;
            ClearFlights();
        }

        /// <summary>Once a frame from the bound HUD (LateUpdate, after its layout): bumps <see cref="LayoutVersion"/> when the symbol moved or resized on screen.</summary>
        internal static void TrackLayout()
        {
            if (s_target == null || s_canvas == null)
            {
                return;
            }

            Vector2 centre = CentreOnScreen();
            Vector2 size = s_target.rect.size * s_canvas.scaleFactor;
            if (float.IsNaN(s_layoutCentre.x)
                || (centre - s_layoutCentre).sqrMagnitude > LayoutTolerance * LayoutTolerance
                || Mathf.Abs(size.x - s_layoutSize.x) > LayoutTolerance
                || Mathf.Abs(size.y - s_layoutSize.y) > LayoutTolerance)
            {
                s_layoutCentre = centre;
                s_layoutSize = size;
                LayoutVersion++;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetStatics()
        {
            Arrived = null;
            s_view = null;
            s_target = null;
            s_canvas = null;
            s_boundFrame = -1;
            s_layoutCentre = new Vector2(float.NaN, float.NaN);
            s_layoutSize = Vector2.zero;
            LayoutVersion = 0;
            ClearFlights();
        }

        // ================================================================== internals

        private static bool ViewLive()
        {
            return s_view != null && s_target != null;
        }

        private static bool Ready()
        {
            return ViewLive() && s_canvas != null && Time.frameCount > s_boundFrame && s_canvas.isActiveAndEnabled &&
                   s_target.gameObject.activeInHierarchy && s_view.GoldTargetVisible;
        }

        private static Vector2 CentreOnScreen()
        {
            Camera camera = s_canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : s_canvas.worldCamera;
            Vector3 world = s_target.TransformPoint(s_target.rect.center);
            return RectTransformUtility.WorldToScreenPoint(camera, world);
        }

        private static void NotifyFlights()
        {
            if (ViewLive())
            {
                s_view.GoldFlightsChanged();
            }
        }

        private static void Push(int amount, float now)
        {
            if (s_count == FlightCapacity)
            {
                int newest = (s_head + s_count - 1) % FlightCapacity;
                s_amount[newest] += amount;
                s_departAt[newest] = now;
                return;
            }

            int slot = (s_head + s_count) % FlightCapacity;
            s_amount[slot] = amount;
            s_departAt[slot] = now;
            s_count++;
        }

        private static void PopHead()
        {
            s_amount[s_head] = 0;
            s_head = (s_head + 1) % FlightCapacity;
            s_count--;
        }

        private static int ReleaseStale(float now)
        {
            int released = 0;
            while (s_count > 0 && now - s_departAt[s_head] >= StaleSeconds)
            {
                released += s_amount[s_head];
                PopHead();
            }

            InFlight -= released;
            return released;
        }

        private static void ClearFlights()
        {
            for (int i = 0; i < s_count; i++)
            {
                s_amount[(s_head + i) % FlightCapacity] = 0;
            }

            s_head = 0;
            s_count = 0;
            InFlight = 0;
        }
    }

    /// <summary>
    /// WP-094: the HUD behind <see cref="HudGoldTarget"/> (RunHudScreen implements it explicitly). Internal: S-04 and every
    /// other caller talk to the static surface only.
    /// </summary>
    internal interface IHudGoldView
    {
        /// <summary>The symbol can be flown to now (not hidden by a panel, the player not down).</summary>
        bool GoldTargetVisible { get; }

        /// <summary>The ledger changed (a departure, a stale release, a reset): re-read balance − InFlight.</summary>
        void GoldFlightsChanged();

        /// <summary>A coin landed with <paramref name="amount"/>: re-read the counter and punch the symbol.</summary>
        void GoldArrived(int amount);
    }
}
