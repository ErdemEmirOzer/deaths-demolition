namespace DD.Prototype.Audio
{
    /// <summary>
    /// One entry per synthesised sound the prototype can make.
    ///
    /// This is NOT the same list as <c>CombatAudioEvent</c> and must not be merged with
    /// it. The event list is the gameplay contract - "something happened". This list is
    /// the sound-design vocabulary - "this is what it sounds like". The two differ in
    /// both directions and that difference is where the design lives:
    ///
    ///   * one event, several sounds: ClassicWeaponFire resolves to RifleFire, SmgFire or
    ///     ShotgunFire from the intensity the weapon sent (see AudioMixPlan);
    ///     ArmorHit resolves to ArmorImpact or ArmorCrack from how hard the plate was hit,
    ///     which is REQ-129's first two rungs;
    ///   * one sound, several events: nothing today, but the indirection costs nothing
    ///     and means retuning a sound never touches a gameplay file;
    ///   * sounds with no combat event at all: everything under META below. The request
    ///     asks for player damage, gold, round countdown and UI navigation, and none of
    ///     those belong in a combat enum owned by the weapon code.
    ///
    /// The integer values are the synthesis seed, so they must stay stable: changing a
    /// number changes what that sound is, permanently.
    /// </summary>
    public enum AudioSoundId
    {
        None = 0,

        // ---- weapon mechanics -------------------------------------------------
        RifleFire = 10,
        SmgFire = 11,
        ShotgunFire = 12,
        HeavyFire = 13,
        DryFire = 14,
        ReloadStart = 15,
        ReloadInsert = 16,
        ReloadEnd = 17,
        HeavyMotorLoop = 18,
        HeavySpinDown = 19,

        // ---- surface impacts --------------------------------------------------
        ImpactDefault = 30,
        ImpactStone = 31,
        ImpactWood = 32,
        ImpactFoliage = 33,
        ImpactWater = 34,
        ImpactMetal = 35,
        ImpactDirt = 36,
        ImpactPaper = 37,

        // ---- target -----------------------------------------------------------
        FleshHit = 50,
        CriticalHit = 51,

        /// <summary>REQ-129 rung 1: the plate takes a blow and holds.</summary>
        ArmorImpact = 52,

        /// <summary>REQ-129 rung 2: the plate is failing. Same family, added crackle.</summary>
        ArmorCrack = 53,

        /// <summary>REQ-129 rungs 3 and 4 in one clip: the plate shears off, then the debris falls.</summary>
        ArmorBreak = 54,

        EnemyDeath = 55,
        HeavyEnemyDeath = 56,

        // ---- upgrade branches -------------------------------------------------
        Ricochet = 70,
        ChainArc = 71,
        Explosion = 72,

        // ---- special weapon lifecycle ----------------------------------------
        SwSpawnGong = 80,
        SwBeaconLoop = 81,
        SwPickup = 82,
        SwFinalSeconds = 83,
        SwExpired = 84,
        SwGroundExpired = 85,

        // ---- confirmation -----------------------------------------------------
        HitMarker = 90,
        KillMarker = 91,

        // ---- META: not owned by the combat event contract ---------------------
        PlayerDamage = 110,
        PlayerDeath = 111,
        GoldPickup = 112,
        RoundCountdownTick = 113,
        RoundStart = 114,
        RoundComplete = 115,
        UiNavigate = 116,
        UiConfirm = 117,
        UiBack = 118,
        UiDenied = 119,
        UpgradePurchase = 120,

        // ---- ambience beds (looping) -----------------------------------------
        AmbWind = 140,
        AmbLeaves = 141,
        AmbWater = 142,
        AmbPalaceTone = 143,
        AmbTensionLow = 144,
        AmbTensionHigh = 145,

        // ---- ambience one-shots ----------------------------------------------
        AmbWoodCreak = 150,
        AmbMetalRing = 151,
        AmbBambooKnock = 152,
        AmbStoneSettle = 153,

        // ---- WP-021 · zombie vocal set (DEC-067: physical, no supernatural) ---------
        // Windup = attack telegraph (>= 0.3 s before the swing, positional, REQ-104 audible
        // readability); Attack = the swing itself; Hit = pain grunt; Death = the collapse.
        ZombieWindupStandard = 160,
        ZombieAttackStandard = 161,
        ZombieHitStandard = 162,
        ZombieDeathStandard = 163,
        ZombieWindupElite = 164,
        ZombieAttackElite = 165,
        ZombieHitElite = 166,
        ZombieDeathElite = 167,
        ZombieWindupUltra = 168,
        ZombieAttackUltra = 169,
        ZombieHitUltra = 170,
        ZombieDeathUltra = 171,

        // ---- WP-021 · gold (three moments of one loop) ---------------------------
        GoldSpawn = 175,
        GoldMagnet = 176,

        // ---- WP-021 · regional ambience beds (DEC-066: K/D/G/B + centre) + common -
        AmbCommon = 180,
        AmbRegionCenter = 181,
        AmbRegionNorth = 182,
        AmbRegionEast = 183,
        AmbRegionSouth = 184,
        AmbRegionWest = 185,

        // ---- WP-021 · hybrid music layers (DEC-064/065) -------------------------
        MusicBase = 190,
        MusicPercussion = 191,
        MusicTension = 192,
        MusicAccent = 193,

        // ---- WP-021 v003 · coverage matrix (EVENT-COVERAGE-MATRIX.md) ------------
        FootstepStone = 200,
        FootstepWood = 201,
        FootstepDirt = 202,
        FootstepFoliage = 203,
        FootstepWater = 204,
        ZombieStepLight = 205,
        ZombieStepHeavy = 206,
        ZombieIdle = 207,
        AttackImpactBody = 208,
        ZombieStagger = 209,
        ZombieRise = 210,
        ArmorPieceDrop = 211,
        BreakCeramic = 212,
        AmbDrip = 213,
        UiTransition = 214,

        // ---- WP-021C · tier vocal set completion (B·B·B; AP-011 v005, derived, no credits)
        ZombieWindupSpecial = 220,
        ZombieAttackSpecial = 221,
        ZombieHitSpecial = 222,
        ZombieDeathSpecial = 223,
        ZombieIdleSpecial = 224,
        ZombieIdleElite = 225,
        ZombieIdleUltra = 226,
        /// <summary>anim.rise_end: the body is up and has seen you (per tier).</summary>
        ZombieAggroStandard = 227,
        ZombieAggroSpecial = 228,
        ZombieAggroElite = 229,
        ZombieAggroUltra = 230,

        // ---- WP-021C · UI event set (WP-022B UiEvents; EVENT-COVERAGE-MATRIX §UI) ---
        UiHover = 240,
        UiPaneOpen = 241,
        UiSelect = 242,
        UiSliderTick = 243,
        UiToggle = 244,
        UiDialogOpen = 245,
        UiReveal = 246,
        HudLowHealth = 247,
        HudMagazineEmpty = 248,
        HudThreatEncircled = 249,
        HudThreatRear = 250,
        HudThreatStrong = 251,
        UiReroll = 252,
        UiLock = 253,
        UiLevelUp = 254,
        UiUnlock = 255,
        UiAchievement = 256,
        UiNewBest = 257,

        // ---- WP-048 · Audio Wave 3 (AP-011 v007, derived, 0 cr): weapon foley per roster weapon (WP-032C clip
        // events), pickup, armour break per region (WP-040), AP-017 breakables, footstep completion (AP-015P), Look B ambience
        WeaponAEject = 260,
        WeaponAChamber = 261,
        WeaponAMagOut = 262,
        WeaponAMagIn = 263,
        WeaponASwitch = 264,
        WeaponALowered = 265,
        WeaponARaised = 266,
        WeaponBEject = 267,
        WeaponBChamber = 268,
        WeaponBMagOut = 269,
        WeaponBMagIn = 270,
        WeaponBSwitch = 271,
        WeaponBLowered = 272,
        WeaponBRaised = 273,
        WeaponCEject = 274,
        WeaponCChamber = 275,
        WeaponCMagOut = 276,
        WeaponCMagIn = 277,
        WeaponCSwitch = 278,
        WeaponCLowered = 279,
        WeaponCRaised = 280,
        PickupGrab = 281,
        PickupDone = 282,
        ArmorBreakHelmet = 283,
        ArmorBreakBody = 284,
        ArmorBreakLeg = 285,
        BreakLanternStone = 286,
        BreakLanternPaper = 287,
        BreakPlanter = 288,
        BreakFence = 289,
        BreakBale = 290,
        BreakBench = 291,
        BreakSign = 292,
        BreakShoji = 293,
        FootstepWaterShallow = 294,
        FootstepWaterDeep = 295,
        FootstepGravel = 296,
        FootstepPavement = 297,
        AmbThunderDistant = 298,
        AmbDripSlate = 299,
        AmbPondBed = 300,

        // ---- WP-057 · music v2 (AP-011 v008, derived from the WP-021B stems, 0 cr) ---------
        StingerRoundBreak = 301,
        StingerSpecialPickup = 302,
        StingerDeath = 303,
        StingerRunEnd = 304,
        MusicMenu = 305,
        MusicResults = 306,

        // ---- WP-058 · Special Weapon usage (WP-056 SpecialUsageEvent; AP-011 v009, derived, 0 cr) — Kagura / Ofuda / Tide
        SwFireKagura = 310,
        SwFireOfuda = 311,
        SwFireTide = 312,
        SwFieldKagura = 313,
        SwFieldOfuda = 314,
        SwFieldTide = 315,
        SwFieldEndKagura = 316,
        SwFieldEndOfuda = 317,
        SwFieldEndTide = 318,
        SwHitKagura = 319,
        SwHitOfuda = 320,
        SwHitTide = 321,

        // ---- WP-058C · enemy Special reactions (WP-055 sw_push / sw_web / sw_pull beats; AP-011 v010, derived, 0 cr)
        ZombieReactPush = 330,
        ZombieReactWeb = 331,
        ZombieReactPull = 332
    }
}
