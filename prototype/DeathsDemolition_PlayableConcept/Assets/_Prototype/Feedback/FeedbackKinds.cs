namespace DD.Prototype.Feedback
{
    /// <summary>
    /// What a bullet hit. Drives which impact effect and which impact sound plays
    /// (step 5 of the twelve-step hit chain, REQ-121).
    /// Resolved from a <see cref="SurfaceTag"/> when the struck collider carries one,
    /// otherwise <see cref="Default"/>.
    /// </summary>
    public enum SurfaceKind
    {
        Default = 0,
        Stone = 1,
        Wood = 2,
        Foliage = 3,
        Water = 4,
        Metal = 5,
        Dirt = 6,
        Paper = 7,
        Flesh = 8,
        Armor = 9
    }

    /// <summary>
    /// REQ-125 feedback hierarchy, in increasing intensity. Never reorder: every
    /// budget, shake and hit-stop decision compares these as integers, and the
    /// request fixes the order as
    /// flesh &lt; armor &lt; armor break &lt; critical &lt; death &lt; Elite/Ultra death.
    /// </summary>
    public enum HitFeedbackTier
    {
        FleshHit = 0,
        ArmorHit = 1,
        ArmorBreak = 2,
        CriticalHit = 3,
        Death = 4,
        HeavyDeath = 5
    }

    /// <summary>
    /// How heavy the firing weapon feels. Set by the weapon, consumed by the feedback
    /// director; it is the "silahin gucu" input of REQ-122 and the gate that keeps
    /// automatic fire out of hit-stop (REQ-127).
    /// </summary>
    public enum WeaponPower
    {
        Light = 0,
        Medium = 1,
        Heavy = 2
    }

    /// <summary>
    /// Why the camera is being shaken. The camera owner (G-02) may give each profile a
    /// different frequency curve; amplitude is already clamped by
    /// <see cref="CameraShakeBridge"/> before the handler ever sees it (REQ-126).
    /// </summary>
    public enum ShakeProfile
    {
        ClassicFire = 0,
        HeavyFire = 1,
        ShotgunFire = 2,
        ArmorBreak = 3,
        HeavyDeath = 4,
        Explosion = 5,
        PlayerDamage = 6
    }

    /// <summary>
    /// How much death spectacle a target deserves. Mirrors the enemy tier list but is
    /// deliberately a separate, feedback-only concept so the Feedback layer never has
    /// to reference the enemy implementation.
    /// </summary>
    public enum FeedbackWeight
    {
        Standard = 0,
        Special = 1,
        Elite = 2,
        Ultra = 3
    }

    /// <summary>
    /// OPTIONAL integration point for the enemy implementation (G-04).
    ///
    /// If an enemy component implements this, the feedback director uses it verbatim to
    /// decide whether a death is an ordinary death or an Elite/Ultra death (REQ-125).
    /// If nothing implements it, the director falls back to a bounds-size heuristic,
    /// which is correct for this prototype only because the request makes Elites and
    /// Ultras visibly larger.
    ///
    /// One line on the enemy root is all that is needed:
    ///     public FeedbackWeight Weight =&gt; _tier switch { ... };
    /// </summary>
    public interface IFeedbackWeightSource
    {
        FeedbackWeight Weight { get; }
    }
}
