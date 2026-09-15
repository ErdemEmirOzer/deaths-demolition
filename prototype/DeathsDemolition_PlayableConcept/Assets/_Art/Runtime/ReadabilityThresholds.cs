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
    }
}
