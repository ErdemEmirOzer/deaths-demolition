namespace DD.Prototype.Difficulty
{
    /// <summary>
    /// WP-102 (R4, user note 2): the three difficulty modes. Normal is the game as of r3-final-b46a3ca4 up to round 20 (from R21 the late-game
    /// health curve and Elite / Ultra share apply to every mode, C-00 KARAR-WP104 §11.3); Hard is faster,
    /// smarter and hits harder; Demolition is built so nobody gets far — very fast, path-cutting crowds, big tiers early and running.
    /// The numeric value is persisted (profile / score keys, S-04) — never reorder.
    /// </summary>
    public enum DifficultyMode
    {
        Normal = 0,
        Hard = 1,
        Demolition = 2
    }

    /// <summary>WP-102: the per-life AI role of a non-flanking body (Normal: always Chase).</summary>
    public enum ZombieAiRole
    {
        /// <summary>Walks at the target (or its WP-054 flank goal).</summary>
        Chase = 0,
        /// <summary>Predictive pursuit: walks at where the player will be (player + velocity × lead).</summary>
        Intercept = 1,
        /// <summary>Path cutter: walks to a point ahead of the player's escape direction when it can get there first.</summary>
        Cut = 2
    }
}
