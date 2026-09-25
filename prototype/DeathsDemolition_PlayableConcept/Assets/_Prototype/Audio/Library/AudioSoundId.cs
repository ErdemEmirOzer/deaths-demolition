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

        // ---- WP-065C · AP-017 Phase 2b fūrin wind chimes (wind-driven positional tinkle, Look B ambience; AP-011 v014)
        AmbFurin = 340,

        // ---- WP-068 slice 11 · UI v3 material set (rin / washi / lacquer-go-hyōshigi / hanko / fusuma / soroban; AP-011 v015)
        UiPressDown = 350,
        UiTransitionOut = 351,
        UiCurtainClose = 352,
        UiCurtainOpen = 353,
        UiPauseOpen = 354,
        UiPauseClose = 355,
        UiBootSeal = 356,
        UiCardDealt = 357,
        UiCardFlipped = 358,
        UiStampLanded = 359,
        UiStampRare = 360,
        UiStampEpic = 361,
        UiCounterRoll = 362,
        UiCounterSettled = 363,
        UiHoldRub = 364,
        UiInkBleed = 365,
        HudNewBestRound = 366,
        HudEliteDown = 367,
        HudUltraDown = 368,

        // ---- WP-072 B1 · weapon fire v3 (S-02 WP-069 audioIds; 5-layer dry pre-mix, tail per acoustic space; AP-011 v016)
        // One-shot = full dry shot. AR1/SMG1/LMG1 (auto ≥ 600 RPM) add a per-shot Transient (mechanism + ring)
        // over an RPM-locked body Loop; their tail plays once on release (WeaponFireAudio).
        WeaponAr1Fire = 400,
        WeaponAr1Transient = 401,
        WeaponAr1Loop = 402,
        WeaponAr2Fire = 403,
        WeaponSmg1Fire = 404,
        WeaponSmg1Transient = 405,
        WeaponSmg1Loop = 406,
        WeaponSmg2Fire = 407,
        WeaponSg1Fire = 408,
        WeaponSg2Fire = 409,
        WeaponLmg1Fire = 410,
        WeaponLmg1Transient = 411,
        WeaponLmg1Loop = 412,
        WeaponLmg2Fire = 413,
        WeaponSr1Fire = 414,
        WeaponRl1Fire = 415,

        // ---- WP-072 B1 · wet-only space tails per weapon class × acoustic space (Open / Corridor / Hall)
        WeaponTailArOpen = 420,
        WeaponTailArCorridor = 421,
        WeaponTailArHall = 422,
        WeaponTailSmgOpen = 423,
        WeaponTailSmgCorridor = 424,
        WeaponTailSmgHall = 425,
        WeaponTailSgOpen = 426,
        WeaponTailSgCorridor = 427,
        WeaponTailSgHall = 428,
        WeaponTailLmgOpen = 429,
        WeaponTailLmgCorridor = 430,
        WeaponTailLmgHall = 431,
        WeaponTailSrOpen = 432,
        WeaponTailSrCorridor = 433,
        WeaponTailSrHall = 434,
        WeaponTailRlOpen = 435,
        WeaponTailRlCorridor = 436,
        WeaponTailRlHall = 437,

        // ---- WP-072 B2 · per-weapon reload / mechanism foley (S-02 WP-069 §3 event sets, WP-071 names; AP-011 v017)
        FoleyAr1MagOut = 440,
        FoleyAr1MagIn = 441,
        FoleyAr1Chamber = 442,
        FoleyAr1Charge = 443,
        FoleyAr2MagOut = 444,
        FoleyAr2MagIn = 445,
        FoleyAr2Charge = 446,
        FoleySmg1MagOut = 447,
        FoleySmg1MagIn = 448,
        FoleySmg1Chamber = 449,
        FoleySmg2MagOut = 450,
        FoleySmg2MagIn = 451,
        FoleySmg2Chamber = 452,
        FoleySg1ShellIn = 453,
        FoleySg1Eject = 454,
        FoleySg1Chamber = 455,
        FoleySg2Eject = 456,
        FoleySg2ShellIn = 457,
        FoleySg2Chamber = 458,
        FoleyLmg1BoxOpen = 459,
        FoleyLmg1BeltSeat = 460,
        FoleyLmg1BoxClose = 461,
        FoleyLmg1Charge = 462,
        FoleyLmg2BoxOpen = 463,
        FoleyLmg2BeltSeat = 464,
        FoleyLmg2BoxClose = 465,
        FoleyLmg2Charge = 466,
        FoleySr1MagOut = 467,
        FoleySr1MagIn = 468,
        FoleySr1BoltUp = 469,
        FoleySr1BoltBack = 470,
        FoleySr1BoltFwd = 471,
        FoleySr1BoltDown = 472,
        FoleyRl1RocketIn = 473,
        FoleyRl1RocketSeat = 474,

        // ---- WP-072 B2 · handling by weight family (Light SMG · Medium AR/SG/SR · Heavy LMG/RL); per-weapon pitch at runtime
        FoleyLightSwitch = 475,
        FoleyLightRaised = 476,
        FoleyLightLowered = 477,
        FoleyLightAds = 478,
        FoleyLightDryFire = 479,
        FoleyMediumSwitch = 480,
        FoleyMediumRaised = 481,
        FoleyMediumLowered = 482,
        FoleyMediumAds = 483,
        FoleyMediumDryFire = 484,
        FoleyHeavySwitch = 485,
        FoleyHeavyRaised = 486,
        FoleyHeavyLowered = 487,
        FoleyHeavyAds = 488,
        FoleyHeavyDryFire = 489,

        // ---- WP-072 B2 · casing landings (caliber family × surface; LMG belt link)
        CasingRifleStone = 490,
        CasingRifleWood = 491,
        CasingRifleDirt = 492,
        CasingPistolStone = 493,
        CasingPistolWood = 494,
        CasingPistolDirt = 495,
        CasingShellStone = 496,
        CasingShellWood = 497,
        CasingShellDirt = 498,
        CasingLink = 499,

        // ---- WP-072 B3 · projectile impact = SurfaceKind (0–7) × WeaponCaliber (Light/Medium/Heavy): 500 + surface·3 + calibre (AP-011 v018)
        ImpactV3DefaultLight = 500,
        ImpactV3DefaultMedium = 501,
        ImpactV3DefaultHeavy = 502,
        ImpactV3StoneLight = 503,
        ImpactV3StoneMedium = 504,
        ImpactV3StoneHeavy = 505,
        ImpactV3WoodLight = 506,
        ImpactV3WoodMedium = 507,
        ImpactV3WoodHeavy = 508,
        ImpactV3FoliageLight = 509,
        ImpactV3FoliageMedium = 510,
        ImpactV3FoliageHeavy = 511,
        ImpactV3WaterLight = 512,
        ImpactV3WaterMedium = 513,
        ImpactV3WaterHeavy = 514,
        ImpactV3MetalLight = 515,
        ImpactV3MetalMedium = 516,
        ImpactV3MetalHeavy = 517,
        ImpactV3DirtLight = 518,
        ImpactV3DirtMedium = 519,
        ImpactV3DirtHeavy = 520,
        ImpactV3PaperLight = 521,
        ImpactV3PaperMedium = 522,
        ImpactV3PaperHeavy = 523,

        // ---- WP-072 B4 · DEC-094 Specials SW_ARC (Raiju chain lightning) / SW_GEL (Namekuji slick gel), S-02 WP-069 §6/§7 events (AP-011 v019)
        SwArcCharge = 530,
        SwArcFire = 531,
        SwArcSphereLoop = 532,
        SwArcChainJump = 533,
        SwArcShockLoop = 534,
        SwArcShockEnd = 535,
        SwArcCellOut = 536,
        SwArcCellIn = 537,
        SwArcReady = 538,
        SwGelFire = 539,
        SwGelFlightLoop = 540,
        SwGelHit = 541,
        SwGelHitFlesh = 542,
        SwGelPuddleCreate = 543,
        SwGelPuddleLoop = 544,
        SwGelPuddleExpire = 545,
        SwGelSlideEnemy = 546,
        SwGelSlidePlayer = 547,
        SwGelCanisterOut = 548,
        SwGelCanisterIn = 549,
        SwGelReady = 550,

        // ---- WP-072 B5 · launcher (STANDARDS §7): rocket flight loop + explosion by listener distance (AP-011 v020)
        RocketFlightLoop = 560,
        ExplosionNear = 561,
        ExplosionMid = 562,
        ExplosionFar = 563,

        // ---- WP-072 · WP-069 B3 rl_1 cluster bomblet + dud (AP-011 v021)
        BombletPop = 564,
        RocketDud = 565
    }
}
