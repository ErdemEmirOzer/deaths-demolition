using UnityEngine;

namespace DD.Art
{
    /// <summary>
    /// WP-015 · The official readability thresholds (user decision 2026-09-15, DEC-077;
    /// REQ-104/REQ-105 acceptance criteria). Until WP-015 the tests carried working
    /// thresholds "not locked here"; from WP-015 they assert exactly these numbers, with the
    /// methods they were measured by:
    ///  - Route occlusion (WP-011 capsule method, WP-014 world test): a 1.8 m capsule at every
    ///    2 m route sample, five axial rays from the gameplay rig (0, +15, −13); a sample whose
    ///    rays the SOLID environment blocks by more than <see cref="OcclusionRatioMax"/> is a
    ///    failure, and the number of such samples must be zero. Fade-mesh occluders (they turn
    ///    transparent) and culm-thin capsules (radius ≤ 0.15 m; they hide none of a 0.7 m body)
    ///    are reported separately and are not solid.
    ///  - Player/environment contrast (WP-013 method): Michelson contrast of the enemy-proxy
    ///    capsule against its surround at the gameplay pose with fog, particles and post-fx on
    ///    must be at least <see cref="CapsuleContrastMin"/>.
    /// </summary>
    public static class ReadabilityThresholds
    {
        public const string Decision = "DEC-077";

        /// <summary>Fraction of a sample capsule's axial rays the solid environment may block; more = the sample fails; failing samples allowed = 0.</summary>
        public const float OcclusionRatioMax = 0.5f;

        /// <summary>Minimum Michelson contrast, capsule vs surround, atmosphere on.</summary>
        public const float CapsuleContrastMin = 0.06f;

        /// <summary>Culm-thin capsule radius bound: at or below this the collider is a stalk, not a solid occluder.</summary>
        public const float ThinCapsuleRadius = 0.15f;

        /// <summary>Frame-difference sum (|Δr|+|Δg|+|Δb|) above which a pixel changed between the full frame and the reference.</summary>
        public const int FigureDiffThreshold = 24;

        /// <summary>Per-channel delta spread at or below which a uniform darkening is read as a cast shadow / AO, not as body.</summary>
        public const int ShadowSpreadMax = 10;

        /// <summary>
        /// DEC-077 figure mask (WP-038E, P-06 AP-007D measurement; WP-038G shares it with the QA gate): a pixel is FIGURE when
        /// the frame differs from the reference without the figure AND the change is not a uniform darkening — a cast shadow
        /// or SSAO drops the luma while the per-channel deltas stay within <see cref="ShadowSpreadMax"/> of each other, a body
        /// pixel replaces the ground colour. The plain difference counted the crowd's ground shadows as crowd (60–68 % of
        /// the pixels) and dragged the crowd luma down as the crowd grew.
        /// </summary>
        public static bool[] FigureDiff(Color32[] full, Color32[] without)
        {
            var mask = new bool[full.Length];
            int n = Mathf.Min(full.Length, without.Length);
            for (int i = 0; i < n; i++)
            {
                int dr = full[i].r - without[i].r, dg = full[i].g - without[i].g, db = full[i].b - without[i].b;
                int sum = Mathf.Abs(dr) + Mathf.Abs(dg) + Mathf.Abs(db);
                if (sum <= FigureDiffThreshold) continue;
                bool darker = dr <= 0 && dg <= 0 && db <= 0;
                int spread = Mathf.Max(dr, Mathf.Max(dg, db)) - Mathf.Min(dr, Mathf.Min(dg, db));
                mask[i] = !(darker && spread <= ShadowSpreadMax);
            }

            return mask;
        }
    }
}
