namespace DD.Prototype.Core.Config
{
    /// <summary>PROTOTYPE-ONLY placeholder weapon roster.</summary>
    public enum ClassicWeaponId
    {
        AssaultRifle = 0,
        AutoSmg = 1,
        CombatShotgun = 2
    }

    /// <summary>PROTOTYPE-ONLY enemy family versions.</summary>
    public enum EnemyTier
    {
        Standard = 0,
        Special = 1,
        Elite = 2,
        Ultra = 3
    }

    /// <summary>Three independent armour pieces on a zombie.</summary>
    public enum ArmorSlot
    {
        None = 0,
        Helmet = 1,
        Body = 2,
        Leg = 3
    }

    /// <summary>Hit region resolved from the collider that was struck.</summary>
    public enum BodyRegion
    {
        Head = 0,
        UpperBody = 1,
        LowerBody = 2
    }

    /// <summary>
    /// How gold reaches the player. Not decided for the real product; switchable
    /// from the F1 debug panel so all three can be felt in one session.
    /// </summary>
    public enum PrototypeGoldCollectionMode
    {
        DirectCredit = 0,
        ContactPickup = 1,
        ProximityMagnet = 2
    }

    /// <summary>PROTOTYPE-ONLY upgrade branches offered on the round-end screen.</summary>
    public enum UpgradeBranch
    {
        Piercing = 0,
        Ricochet = 1,
        Explosion = 2,
        Chain = 3,
        FireRate = 4,
        ReloadSpeed = 5,
        MovementSpeed = 6,
        MaximumHealth = 7,
        RegenerationDelay = 8,
        RegenerationRate = 9,

        // WP-077 (accepted from WP-069 §6, M4 arsenal v3): weapon-core branches for sr_1 / lmg_2
        // (ArmorBreak), smg_2 / sg_2 (Impact) and rl_1 (Cluster). Values append; 0–9 never move
        // (profile, scene and asset serialisation are by int). Held out of the draw until their
        // behaviour hooks land (UpgradeCardCatalog.s_unbacked).
        ArmorBreak = 10,
        Impact = 11,
        Cluster = 12
    }

    /// <summary>PROTOTYPE-ONLY skin identifiers. Cosmetic only, zero gameplay effect.</summary>
    public enum SkinId
    {
        IvoryCrimson = 0,
        IndigoBlack = 1,
        MossCream = 2
    }

    /// <summary>How an achievement is evaluated.</summary>
    public enum AchievementTrigger
    {
        FirstKill = 0,
        ArmorPieceBroken = 1,
        ReachRound = 2,
        KillTier = 3
    }
}
