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
        // WP-047E · AP-014C (staged as AP010C)
        public const string SwTideWell = "sw_tide_well";
        public const string SwTideImplode = "sw_tide_implode";
        public const string SwOfudaDartSheet = "sw_ofuda_dart";
        public const string BarrelHeatDistortTile = "barrel_heat_distort_tile";
        public const string ChainArcSheet = "chain_arc_sheet";
        public const string OfudaSeal = "ofuda_seal";

        // ---- AP-014 Special Weapon use sheets (`SpecialWeaponVisualLibrary.FindSheet(usage)`, WP-037B) — runtime materials, not catalog rows
        public const string SwKaguraWave = "sw_kagura_wave";
        public const string SwOfudaDart = "sw_ofuda_dart";
        public const string SwOfudaWeb = "sw_ofuda_web";
        public const string SwOfudaCollapse = "sw_ofuda_collapse";
    }
}
