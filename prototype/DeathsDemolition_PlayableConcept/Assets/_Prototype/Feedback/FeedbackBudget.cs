using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>Effect families that are budgeted independently.</summary>
    public enum FeedbackCategory
    {
        Impact = 0,
        Blood = 1,
        ArmorHit = 2,
        ArmorBreak = 3,
        Death = 4,
        DamageNumber = 5,
        Tracer = 6,
        MuzzleFlash = 7,
        Link = 8,
        Explosion = 9,

        /// <summary>
        /// G-21. The three Special Weapons' own visuals - pressure fronts, talisman
        /// seals and web links.
        ///
        /// A SEPARATE FAMILY ON PURPOSE. The player asked for Special Weapon effects
        /// that are visibly stronger than the classic weapons', and the cheap way to
        /// deliver that would have been to raise the shared ceilings. That would spend
        /// REQ-128 readability - the thing the crowd budget exists to protect - in a
        /// forty-enemy scene, for a weapon that is on screen maybe eleven seconds a
        /// round. Giving the Special Weapon its own track means it can be loud inside
        /// its own family and still cannot take a single slot away from blood, impacts,
        /// armour breaks or deaths.
        /// </summary>
        SpecialWeapon = 10
    }

    /// <summary>
    /// REQ-128 in one object: "limit the number of simultaneous impact, blood, armour
    /// and death effects so a crowd stays readable".
    ///
    /// Three independent gates, because one is not enough:
    ///
    ///  1. CONCURRENT CEILING - the pool capacity, taken from
    ///     <c>PrototypeConfig.Feedback.Max*Effects</c> and enforced by
    ///     <c>PoolExhaustedPolicy.RecycleOldest</c>. This bounds what is on screen. It
    ///     is not enough on its own, because recycling means the newest effect always
    ///     wins and forty enemies can churn the whole pool inside one frame.
    ///
    ///  2. PER-FRAME CAP - how many *new* effects of a family may start this frame.
    ///     A shotgun firing eight pellets into a pack cannot open twenty-four bursts in
    ///     one frame; it opens a handful and the rest are dropped silently. Without
    ///     this, gate 1 turns into a strobe.
    ///
    ///  3. PROXIMITY MERGE - a second effect of the same family within
    ///     <see cref="MinSpacing"/> metres and <see cref="SpacingWindow"/> seconds of one
    ///     already playing is dropped. This is the shotgun-on-one-torso case: eight
    ///     pellets should read as one heavy hit, not eight small ones.
    ///
    /// Important events (armour break, death) bypass gate 3 and get a much larger share
    /// of gate 2, which is how the REQ-125 intensity hierarchy survives a crowd: the
    /// loud thing is still the thing you see.
    ///
    /// Frame accounting keys off <c>Time.frameCount</c> rather than an Update call, so
    /// the budget is correct no matter which script order a weapon fires in.
    /// </summary>
    public sealed class FeedbackBudget
    {
        private const int CategoryCount = 11;
        private const int RecentCapacity = 16;

        private sealed class Track
        {
            public int PerFrameCap = 4;
            public float MinSpacing;
            public float SpacingWindow = 0.08f;

            public int UsedThisFrame;
            public int Frame = -1;

            public readonly Vector3[] RecentPositions = new Vector3[RecentCapacity];
            public readonly float[] RecentTimes = new float[RecentCapacity];
            public int Head;

            public int Suppressed;
        }

        private readonly Track[] _tracks = new Track[CategoryCount];
        private float _perFrameFraction = .25f;
        private int _minStartsPerFrame = 2;
        private int _importantStartMultiplier = 2;

        public FeedbackBudget()
        {
            for (int i = 0; i < CategoryCount; i++)
            {
                _tracks[i] = new Track();
            }
        }

        /// <summary>Total drops since the last <see cref="ResetCounters"/>. F1 readout.</summary>
        public int TotalSuppressed { get; private set; }

        /// <summary>
        /// Re-derive the per-frame caps and merge distances from the live ceilings.
        /// Called from the director's ApplyConfig, so an F1 edit lands immediately.
        /// </summary>
        public void Configure(FeedbackCategory category, int concurrentCeiling, float minSpacing, float spacingWindow)
        {
            Track t = _tracks[(int)category];

            // PROTOTYPE-ONLY: a quarter of the concurrent ceiling may start in one
            // frame, never fewer than two. At 60 fps that still fills the ceiling in
            // ~4 frames, which is fast enough to feel instant and slow enough to stop
            // a single shotgun blast owning the entire budget.
            t.PerFrameCap = Mathf.Max(_minStartsPerFrame, Mathf.CeilToInt(concurrentCeiling * _perFrameFraction));
            t.MinSpacing = Mathf.Max(0f, minSpacing);
            t.SpacingWindow = Mathf.Max(0f, spacingWindow);
        }

        public void ConfigurePolicy(float perFrameFraction, int minStartsPerFrame, int importantStartMultiplier)
        {
            _perFrameFraction = Mathf.Clamp(perFrameFraction, .05f, .25f);
            _minStartsPerFrame = Mathf.Clamp(minStartsPerFrame, 1, 2);
            _importantStartMultiplier = Mathf.Clamp(importantStartMultiplier, 1, 2);
        }

        /// <summary>
        /// Ask permission to start one effect of this family at this position.
        /// Returns false when the effect must be dropped.
        /// </summary>
        /// <param name="important">
        /// True for armour breaks, deaths and explosions. Skips the proximity merge and
        /// doubles the per-frame allowance.
        /// </param>
        public bool TryConsume(FeedbackCategory category, Vector3 position, bool important = false)
        {
            Track t = _tracks[(int)category];

            int frame = Time.frameCount;
            if (t.Frame != frame)
            {
                t.Frame = frame;
                t.UsedThisFrame = 0;
            }

            int cap = important ? t.PerFrameCap * _importantStartMultiplier : t.PerFrameCap;
            if (t.UsedThisFrame >= cap)
            {
                t.Suppressed++;
                TotalSuppressed++;
                return false;
            }

            if (!important && t.MinSpacing > 0f && IsCrowded(t, position))
            {
                t.Suppressed++;
                TotalSuppressed++;
                return false;
            }

            t.UsedThisFrame++;
            Remember(t, position);
            return true;
        }

        public int SuppressedFor(FeedbackCategory category) => _tracks[(int)category].Suppressed;

        public void ResetCounters()
        {
            TotalSuppressed = 0;
            for (int i = 0; i < CategoryCount; i++)
            {
                _tracks[i].Suppressed = 0;
            }
        }

        private static bool IsCrowded(Track t, Vector3 position)
        {
            float now = Time.time;
            float sqrSpacing = t.MinSpacing * t.MinSpacing;

            for (int i = 0; i < RecentCapacity; i++)
            {
                if (now - t.RecentTimes[i] > t.SpacingWindow)
                {
                    continue;
                }

                if ((t.RecentPositions[i] - position).sqrMagnitude <= sqrSpacing)
                {
                    return true;
                }
            }

            return false;
        }

        private static void Remember(Track t, Vector3 position)
        {
            t.RecentPositions[t.Head] = position;
            t.RecentTimes[t.Head] = Time.time;
            t.Head = (t.Head + 1) % RecentCapacity;
        }
    }
}
