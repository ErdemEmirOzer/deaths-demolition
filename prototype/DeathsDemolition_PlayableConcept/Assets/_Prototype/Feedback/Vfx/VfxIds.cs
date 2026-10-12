namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>Catalog ids (file stem without the AP010_ prefix, size and version suffixes). One place, so a typo is a compile error.</summary>
    public static class VfxIds
    {
        public const string ImpactFlesh = "fb_impact_flesh";
        public const string ImpactArmor = "fb_impact_armor";
        public const string ImpactStone = "fb_impact_stone";
        public const string ImpactWood = "fb_impact_wood";
        public const string ImpactEarth = "fb_impact_earth";
        public const string Muzzle = "fb_muzzle";
        public const string Explosion = "fb_explosion";
        /// <summary>WP-047H (AP-010B v004): the asymmetric armour-break sheet — origin uv (0.3, 0.5), the break travels +u.</summary>
        public const string ArmorBreak = "fb_armor_break";
        public const string Shockwave = "fb_shockwave";
        public const string ZombieDeath = "fb_zombiedeath";
        public const string BeaconRingAnim = "beacon_ring_anim";

        public const string DecalImpactFlesh = "decal_impact_flesh";
        public const string DecalImpactArmor = "decal_impact_armor";
        public const string DecalImpactStone = "decal_impact_stone";
        public const string DecalImpactWood = "decal_impact_wood";
        public const string DecalImpactEarth = "decal_impact_earth";
        public const string DecalScorch = "decal_scorch";
        public const string DecalLacquerSplinter = "decal_lacquer_splinter";
        public const string DecalZombieSplat01 = "decal_zombie_splat_01";
        public const string DecalZombieSplat02 = "decal_zombie_splat_02";

        public const string CardShockwave = "card_shockwave";
        public const string CardSmoke = "card_smoke";
        public const string AtlasArmorBreak = "atlas_armorbreak";
        public const string CardGoldSparkle = "card_gold_sparkle";
        public const string CardGoldTrail = "card_gold_trail";

        public const string GoldPackMesh = "goldpack_V01";
        public const string GoldPackAlbedo = "goldpack_albedo";
        public const string GoldPackEmissive = "goldpack_emissive";

        public const string BeaconColumn = "beacon_column";
        public const string BeaconRing = "beacon_ring";
        public const string HudMarker = "hud_marker";
        public const string VignetteDamage = "vignette_damage";

        /// <summary>WP-019b · synthesized by the importer: the droplet material (tint baked, instancing on). Source = AP-010B <c>sheet_blood_droplets</c> when present, else the flesh decal.</summary>
        public const string BloodDroplet = "blood_droplet";

        // ---- AP-010B v003 (blood set A1–A7)
        public const string BloodSplash01 = "fb_blood_splash_01";
        public const string BloodDeath01 = "fb_blood_death_01";
        public const string BloodHeavy01 = "fb_blood_heavy_01";
        public const string BloodDropletSheet = "sheet_blood_droplets";
        public const string BloodPoolAlbedo = "decal_blood_pool_albedo";
        public const string BloodPoolGrowth = "sheet_blood_pool_growth";
        public const string BloodGround01 = "decal_blood_ground_01_albedo";
        public const string BloodWall01 = "decal_blood_wall_01_albedo";

        // ---- AP-010B v003 wave 2 (the ones WP-019b binds; the rest are catalog rows for their owners)
        public const string PlayerDeathOverlay = "player_death_overlay";
        public const string PlayerDeathOverlayPulse = "player_death_overlay_pulse";
        public const string Spark = "spark";
        public const string SparkStreak = "spark_streak";
        public const string LeafBurst = "leaf_burst";
        public const string WaterSplash = "water_splash";
        public const string WaterSplashRing = "water_splash_ring";
        public const string PaperTear = "paper_tear";
        public const string ArmorCrackDust = "armor_crack_dust";
        public const string DeathSpecial = "death_special";
        public const string DeathUltra = "death_ultra";
        public const string BreakableResidue = "breakable_residue";
        public const string WeaponPickupFlash = "weapon_pickup_flash";
        public const string WeaponExpire = "weapon_expire";
        public const string StrawPuff = "straw_puff";
        public const string StrawPuffCloud = "straw_puff_cloud";
        public const string MagDropDust = "mag_drop_dust";
        public const string SpawnDust = "spawn_dust";
        public const string ClawSwipe = "claw_swipe";
        public const string LanternFlame = "lantern_flame";
        public const string DustStep = "dust_step";
        public const string MagDrop = "mag_drop";
        public const string BarrelHeat = "barrel_heat";
        public const string BarrelHeatDistort = "barrel_heat_distort";
        // WP-047E · AP-014C (staged as AP010C). The old Specials' sheets (sw_tide_well / _implode, sw_ofuda_dart, ofuda_seal
        // (_burn), the AP-014 Kagura / Ofuda use sheets) left with WP-073 B6's asset slice: Raiju / Namekuji draw procedurally.
        public const string BarrelHeatDistortTile = "barrel_heat_distort_tile";
        public const string ChainArcSheet = "chain_arc_sheet";

        // ---- WP-101 (R4, AP-041 v001): Codex's premultiplied photographic set (ArtAp041VfxIntake writes the rows from the
        // trimmed AP041-vfx-manifest-v001.json). Top-down muzzle flashes per class × three variants, the muzzle smoke, five
        // impact sheets and four bullet holes each for stone / wood / metal. Ap041Vfx maps classes and surfaces onto them.
        public const string Ap041MuzzlePrefix = "fb_ap041_muzzle_";
        public const string Ap041MuzzlePistolTd01 = "fb_ap041_muzzle_pistol_td_01";
        public const string Ap041MuzzlePistolTd02 = "fb_ap041_muzzle_pistol_td_02";
        public const string Ap041MuzzlePistolTd03 = "fb_ap041_muzzle_pistol_td_03";
        public const string Ap041MuzzleRifleTd01 = "fb_ap041_muzzle_rifle_td_01";
        public const string Ap041MuzzleRifleTd02 = "fb_ap041_muzzle_rifle_td_02";
        public const string Ap041MuzzleRifleTd03 = "fb_ap041_muzzle_rifle_td_03";
        public const string Ap041MuzzleLmgTd01 = "fb_ap041_muzzle_lmg_td_01";
        public const string Ap041MuzzleLmgTd02 = "fb_ap041_muzzle_lmg_td_02";
        public const string Ap041MuzzleLmgTd03 = "fb_ap041_muzzle_lmg_td_03";
        public const string Ap041MuzzleSniperTd01 = "fb_ap041_muzzle_sniper_td_01";
        public const string Ap041MuzzleSniperTd02 = "fb_ap041_muzzle_sniper_td_02";
        public const string Ap041MuzzleSniperTd03 = "fb_ap041_muzzle_sniper_td_03";
        public const string Ap041MuzzlePelletTd01 = "fb_ap041_muzzle_pellet_td_01";
        public const string Ap041MuzzlePelletTd02 = "fb_ap041_muzzle_pellet_td_02";
        public const string Ap041MuzzlePelletTd03 = "fb_ap041_muzzle_pellet_td_03";
        public const string Ap041MuzzleSmoke = "fb_ap041_muzzle_smoke";
        public const string Ap041ImpactStone = "fb_ap041_impact_stone";
        public const string Ap041ImpactWood = "fb_ap041_impact_wood";
        public const string Ap041ImpactEarth = "fb_ap041_impact_earth";
        public const string Ap041ImpactMetal = "fb_ap041_impact_metal";
        public const string Ap041ImpactFlesh = "fb_ap041_impact_flesh";
        public const string Ap041DecalStone01 = "decal_ap041_stone_01";
        public const string Ap041DecalStone02 = "decal_ap041_stone_02";
        public const string Ap041DecalStone03 = "decal_ap041_stone_03";
        public const string Ap041DecalStone04 = "decal_ap041_stone_04";
        public const string Ap041DecalWood01 = "decal_ap041_wood_01";
        public const string Ap041DecalWood02 = "decal_ap041_wood_02";
        public const string Ap041DecalWood03 = "decal_ap041_wood_03";
        public const string Ap041DecalWood04 = "decal_ap041_wood_04";
        public const string Ap041DecalMetal01 = "decal_ap041_metal_01";
        public const string Ap041DecalMetal02 = "decal_ap041_metal_02";
        public const string Ap041DecalMetal03 = "decal_ap041_metal_03";
        public const string Ap041DecalMetal04 = "decal_ap041_metal_04";

        /// <summary>WP-101 · TD flash rows by <see cref="Ap041MuzzleClass"/> (pistol, rifle, lmg, sniper, pellet) × variant 01–03.</summary>
        public static readonly string[][] Ap041MuzzleTd =
        {
            new[] { Ap041MuzzlePistolTd01, Ap041MuzzlePistolTd02, Ap041MuzzlePistolTd03 },
            new[] { Ap041MuzzleRifleTd01, Ap041MuzzleRifleTd02, Ap041MuzzleRifleTd03 },
            new[] { Ap041MuzzleLmgTd01, Ap041MuzzleLmgTd02, Ap041MuzzleLmgTd03 },
            new[] { Ap041MuzzleSniperTd01, Ap041MuzzleSniperTd02, Ap041MuzzleSniperTd03 },
            new[] { Ap041MuzzlePelletTd01, Ap041MuzzlePelletTd02, Ap041MuzzlePelletTd03 }
        };

        /// <summary>WP-101 · Hole rows by set (stone, wood, metal) × variant 01–04.</summary>
        public static readonly string[][] Ap041Decals =
        {
            new[] { Ap041DecalStone01, Ap041DecalStone02, Ap041DecalStone03, Ap041DecalStone04 },
            new[] { Ap041DecalWood01, Ap041DecalWood02, Ap041DecalWood03, Ap041DecalWood04 },
            new[] { Ap041DecalMetal01, Ap041DecalMetal02, Ap041DecalMetal03, Ap041DecalMetal04 }
        };
    }
}
