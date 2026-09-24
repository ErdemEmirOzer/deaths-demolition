namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-071 · B2 · K3 · the locomotion cadence policy (N5: "feet and body may move slower than the travel speed and
    /// slide to cover the distance"). Clip playback rates stay inside [<see cref="Min"/>, <see cref="Max"/>]; travel beyond
    /// that is covered by sliding. The foot lock (<see cref="FootIkAdapter"/>) engages only at rates ≤ <see cref="LockMaxRate"/>,
    /// where the cadence is honest enough for a planted foot not to fight the slide.
    /// </summary>
    public static class CadenceBand
    {
        public const float Min = 0.75f;   // council 0.85; the Runner's run clip is honest at ×0.75 (4.6 m/s over 6.14) — a higher floor makes the feet outrun the body
        public const float Max = 1.20f;
        public const float LockMaxRate = 1.05f;
        /// <summary>Visible slide accepted per view (share of the travel not carried by the cadence): top-down / TP-FP.</summary>
        public const float SlideTopDown = 0.35f, SlideClose = 0.20f;

        public static float Clamp(float honestRate) => honestRate < Min ? Min : honestRate > Max ? Max : honestRate;

        /// <summary>Share of the travel covered by sliding at <paramref name="rate"/> (0 = stride-matched).</summary>
        public static float SlideFraction(float travelMps, float clipNoSlideMps, float rate)
        {
            if (travelMps <= 0.01f) return 0f;
            float carried = clipNoSlideMps * rate;
            float s = 1f - carried / travelMps;
            return s < 0f ? -s : s;
        }
    }
}
