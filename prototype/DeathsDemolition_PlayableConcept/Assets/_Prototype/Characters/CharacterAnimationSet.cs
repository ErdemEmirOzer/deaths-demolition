using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Characters
{
    /// <summary>
    /// WP-018 · ADR-018-2 · The Animator contract both characters share: parameter names,
    /// state names and the AP-009 clip names. The controllers are GENERATED from the
    /// animation set by <c>ArtAnimatorBuilder</c>; gameplay code only ever writes these
    /// parameters and listens to <see cref="CharacterAnimationEvents"/>.
    /// </summary>
    public static class CharacterAnimatorContract
    {
        // ------------------------------------------------------------- parameters
        /// <summary>WP-016 rig-test 1-D blend (0 idle … 1 walk) — kept for the rig-test controller.</summary>
        public const string Speed = "Speed";
        /// <summary>Zombie: 0 idle … 1 top speed of the version (gait blend / idle↔gait switch).</summary>
        public const string Speed01 = "Speed01";
        /// <summary>Zombie: playback rate of the gait state = ground speed / clip speed_mps (no foot slide).</summary>
        public const string GaitRate = "GaitRate";
        /// <summary>Zombie: 0 shamble_A, 1 shamble_B, 2 shamble_C, 3 run (fit / version).</summary>
        public const string Gait = "Gait";
        /// <summary>Zombie: 0 sway, 1 twitch.</summary>
        public const string IdleVariant = "IdleVariant";
        public const string Attack = "Attack";           // trigger
        public const string AttackStyle = "AttackStyle"; // int: 0 swipe, 1 lunge, 2 sweep, 3 overhead (WP-032B, Ultra)
        public const string Electro = "Electro";           // bool: WP-071 AP-022 s2a — seized by the Raiju arc (hit_electro_loop)
        public const string AttackMirror = "AttackMirror"; // bool: WP-071 B5 — the attack plays mirrored (swipe_L = swipe_R mirrored, S-03 AttackSelector)
        public const string AttackRate = "AttackRate";   // float: clip speed so hit_window_start lands on the tier telegraph
        public const string Stagger = "Stagger";         // trigger (heavy, full body)
        public const string HitLeft = "HitL";            // trigger, override layer
        public const string HitRight = "HitR";           // trigger, override layer
        public const string HitLight = "Hit";            // trigger, override layer (player)
        public const string FlinchHead = "FlinchHead";   // trigger, additive layer
        public const string FlinchTorso = "FlinchTorso"; // trigger, additive layer
        public const string FlinchLeg = "FlinchLeg";     // trigger, additive layer
        public const string Die = "Die";                 // trigger
        public const string DeathStyle = "DeathStyle";   // int: zombie 0 fwd 1 back 2 side 3 kneel; player 0 fwd 1 back
        public const string Rise = "Rise";               // trigger
        public const string Alive = "Alive";             // bool: false after Die (blocks re-entry into locomotion)
        public const string Fire = "Fire";               // trigger, additive layer (player): Fire_<Weapon>
        public const string Pickup = "Pickup";           // trigger (player, override_upper)
        // WP-032C · AP-009C v003 weapon handling (player, override_upper / additive) — S-02 WP-042 drives these through PlayerSkinnedVisual
        public const string Weapon = "Weapon";           // int: ClassicWeaponId (0 A rifle, 1 B smg, 2 C shotgun) — selects Aim_/Reload_/Switch_/Fire_ states
        public const string Reload = "Reload";           // trigger → Reload_<Weapon> at ReloadRate
        public const string ReloadRate = "ReloadRate";   // float: clip seconds / WeaponConfig.ReloadTime
        public const string ReloadEmpty = "ReloadEmpty"; // bool: WP-071 B4b — the v3 set's reload_empty (else reload_tac)
        public const string SwitchOut = "SwitchOut";     // trigger → SwitchOut_<Weapon> (switch_away event)
        public const string SwitchIn = "SwitchIn";       // trigger → SwitchIn_<Weapon> (switch_ready event)
        public const string Lower = "Lower";             // trigger → WeaponLower (lowered event)
        public const string Raise = "Raise";             // trigger → WeaponRaise (raised event)
        public const string Cycle = "Cycle";             // trigger: WP-071 B4c — the v3 set's bolt cycle after a shot (sr_1)
        public const string SwHold = "SwHold";           // int: 0 none, 1 Kagura Bell, 2 Ofuda Loom (SwHold_ loops replace Aim_)
        public const string Hurt = "Hurt";               // trigger (player): HurtStyle 0 = hurt_A (override_upper), 1 = hurt_B (base, gut hit with a step)
        public const string HurtStyle = "HurtStyle";     // int
        // WP-032E · Special Weapon use (S-02 WP-056 SpecialUsageEvent.Fired): additive swing emphasis over the SwHold pose — SwFire_<SwHold>
        public const string SwFire = "SwFire";           // trigger (player, additive layer); SwHold selects the swing (1 bell sweep, 2 loom cast)
        // WP-032C zombie tier reactions / variants
        public const string HitDir = "HitDir";           // int: 0 front, 1 back, 2 left, 3 right (Hit_<dir>_<tier>)
        public const string HitTier = "HitTier";         // int: 0 std, 1 elite, 2 ultra
        public const string RiseVariant = "RiseVariant"; // int: 0 rise (A), 1 rise_B, 2 rise_C
        public const string ArmorBreak = "ArmorBreak";   // trigger (base): ArmorBreak_<region>
        public const string ArmorBreakRegion = "ArmorBreakRegion"; // int: 0 head, 1 torso, 2 leg
        public const string Stumble = "Stumble";         // trigger (base): hit_special_stumble (Runner)
        public const string MoveX = "MoveX";             // player: local strafe velocity / reference speed
        public const string MoveY = "MoveY";             // player: local forward velocity / reference speed
        public const string LocoRate = "LocoRate";       // player: |v| / reference speed (state speed multiplier)
        // WP-032B queue / idle-in-crowd wait (AP-009B idle_queue): bool gate + per-body cycle offset so a queue never shifts in unison
        public const string Waiting = "Waiting";           // bool
        public const string WaitPhase = "WaitPhase";       // float 0..1 (cycle offset of the Wait state)
        // WP-032 telegraph exaggeration bridge (additive upper-body emphasis over the wind-up; DEC-029 events untouched)
        public const string Telegraph = "Telegraph";         // trigger
        public const string TelegraphStyle = "TelegraphStyle"; // int: 0 swipe, 1 lunge, 2 sweep (overhead shares the sweep emphasis)
        public const string TelegraphRate = "TelegraphRate";   // float: 1 / wind-up seconds (the 1 s clip spans the wind-up)

        // ----------------------------------------------------------------- states
        public const string LocomotionState = "Locomotion";
        public const string IdleState = "Idle";
        public const string GaitState = "Gait";
        public const string AttackSwipeState = "Attack_Swipe";
        public const string AttackLungeState = "Attack_Lunge";
        public const string AttackSweepState = "Attack_Sweep";
        public const string AttackOverheadState = "Attack_Overhead";   // WP-032B (AP-009B v002, Ultra)
        public const string AttackBiteGrabState = "Attack_BiteGrab";   // WP-071 (AP-022 slice 1)
        public const string AttackSwipeLState = "Attack_SwipeL";       // WP-071 (AP-022 slice 1)
        public const string ElectroState = "Electro_Loop";             // WP-071 (AP-022 slice 2a)
        public const string WaitState = "Wait";                        // WP-032B (idle_queue)
        // WP-032C states
        public const string AimState = "Aim";                 // Aim_<weapon> (override_upper loop, the rest state while armed)
        public const string ReloadState = "Reload";           // Reload_<weapon>
        public const string SwitchOutState = "SwitchOut";     // SwitchOut_<weapon>
        public const string SwitchInState = "SwitchIn";       // SwitchIn_<weapon>
        // WP-071 B4b (K8 / A6.1): the v3 generic weapon states (Weapon == WeaponPoseV3); the clips are the base set's, a per-weapon
        // AnimatorOverrideController (animSet) swaps the weapon-specific slots
        public const int WeaponPoseV3 = 4;
        public const string AimV3State = "Aim_V3", ReloadTacState = "ReloadTac", ReloadEmptyState = "ReloadEmpty",
            SwitchInV3State = "SwitchIn_V3", SwitchOutV3State = "SwitchOut_V3", FireV3State = "Fire_V3", CycleV3State = "Cycle_V3";
        public const string WeaponLowerState = "WeaponLower";
        public const string WeaponRaiseState = "WeaponRaise";
        public const string SwHoldState = "SwHold";           // SwHold_1 bell, SwHold_2 loom
        public const string HurtState = "Hurt";               // Hurt_A (override_upper), Hurt_B (base)
        public const string ArmorBreakState = "ArmorBreak";   // ArmorBreak_<region>
        public const string StumbleState = "Stumble";
        public const string SwFireState = "SwFire";           // SwFire_1 / SwFire_2 (generated additive clips)
        public const string StaggerState = "Stagger";
        public const string DeathState = "Death";
        public const string RiseState = "Rise";
        public const string PickupState = "Pickup";
        public const string EmptyState = "Empty";
        public const string HitState = "Hit";
        public const string FlinchState = "Flinch";
        public const string FireState = "Fire";

        // ----------------------------------------------------------------- layers
        public const int BaseLayer = 0;
        public const string OverrideUpperLayer = "override_upper";
        public const string AdditiveLayer = "additive";
        public const string AimLayer = "aim_layer";
        public const string TelegraphLayer = "telegraph";   // WP-032
        public const string TelegraphState = "Telegraph";
        public static readonly string[] TelegraphClips = { "DD_Telegraph_Swipe", "DD_Telegraph_Lunge", "DD_Telegraph_Sweep" };   // generated (ArtAnimatorBuilder)
        /// <summary>WP-032E: generated additive Special Weapon swings (bell sweep, loom cast) until AP-009F ships sw_fire clips.</summary>
        public static readonly string[] SwFireClips = { "DD_SwFire_Bell", "DD_SwFire_Loom" };

        // -------------------------------------------------------- AP-009 clip names
        public const string PlayerIdle = "AP009_player_idle";
        public const string PlayerWalkFwd = "AP009_player_walk_fwd";
        public const string PlayerWalkBack = "AP009_player_walk_back";
        public const string PlayerStrafeLeft = "AP009_player_strafe_left";
        public const string PlayerStrafeRight = "AP009_player_strafe_right";
        // WP-032B · AP-009B v002 run cycles (flight phase; the outer ring of the 2-D locomotion blend)
        public const string PlayerRunFwd = "AP009_player_run_fwd";
        public const string PlayerRunBack = "AP009_player_run_back";
        public const string PlayerRunStrafeLeft = "AP009_player_run_strafe_left";
        public const string PlayerRunStrafeRight = "AP009_player_run_strafe_right";
        public const string PlayerFireRecoil = "AP009_player_fire_recoil";
        public const string PlayerHitLight = "AP009_player_hit_light";
        public const string PlayerDeathFwd = "AP009_player_death_fwd";
        public const string PlayerDeathBack = "AP009_player_death_back";
        public const string PlayerPickup = "AP009_player_pickup_special";   // v001 full-body grab (kept in the set; unused by the controller since WP-032C)
        // WP-032C · AP-009C v003
        public const string PlayerPickupUpper = "AP009_player_pickup_upper";
        public const string PlayerSwHoldBell = "AP009_player_sw_hold_bell";
        public const string PlayerSwHoldLoom = "AP009_player_sw_hold_loom";
        /// <summary>AP-009G v007: the Tide Jewel hold (SwHold_3).</summary>
        public const string PlayerSwHoldJewel = "AP009_player_sw_hold_jewel";
        public const string PlayerDeathSide = "AP009_player_death_side";
        public const string PlayerHurtA = "AP009_player_hurt_A";
        public const string PlayerHurtB = "AP009_player_hurt_B";
        public const string PlayerWeaponLower = "AP009_player_weapon_lower";   // A (v003); v004: B/C = AP009_player_<B|C>_weapon_lower
        public const string PlayerWeaponRaise = "AP009_player_weapon_raise";
        /// <summary>WP-032C (v004): the weapon's lower / raise clip — A keeps the unsuffixed v003 name, B / C carry the letter.</summary>
        public static string PlayerWeaponLowerClip(int weapon) => weapon <= 0 ? PlayerWeaponLower : PlayerWeaponClip(weapon, "weapon_lower");
        public static string PlayerWeaponRaiseClip(int weapon) => weapon <= 0 ? PlayerWeaponRaise : PlayerWeaponClip(weapon, "weapon_raise");
        /// <summary>Weapon letter of a ClassicWeaponId index (0 A AssaultRifle, 1 B AutoSmg, 2 C CombatShotgun).</summary>
        public static readonly string[] WeaponLetters = { "A", "B", "C" };
        // WP-071 B4b · AP-022 v3 weapon clips: class slots (shared by the class's weapons) and weapon slots (per weaponId)
        public static readonly string[] WeaponClassSlots = { "idle_aim", "ads_in", "ads_idle", "ads_out", "switch_in", "switch_out" };
        public static readonly string[] WeaponOwnSlots = { "fire", "reload_tac", "reload_empty" };
        /// <summary>The v3 animation sets present (WeaponDefinition.AnimSet keys); the first one is the base controller's default.</summary>
        public static readonly string[] WeaponAnimSetsV3 = { "ar_1", "ar_2", "smg_1", "smg_2", "sr_1", "rl_1", "lmg_1", "lmg_2" };   // WP-071 B4c: + smg_2 / sniper / launcher / LMG TP
        /// <summary>The class of a set (its class clips' prefix): the weapon id's prefix, AP-022's class names for the sniper (snp) and launcher (lch).</summary>
        public static string WeaponClassOf(string animSet)
        {
            int u = animSet.IndexOf('_');
            string c = u > 0 ? animSet.Substring(0, u) : animSet;
            return c == "sr" ? "snp" : c == "rl" ? "lch" : c;
        }

        /// <summary>WP-071 B4c: a set's bolt cycle after a shot (AP022_tp_&lt;set&gt;_bolt_cycle — sr_1).</summary>
        public static string PlayerWeaponCycleClipV3(string animSet) => "AP022_tp_" + animSet + "_bolt_cycle";
        public static string PlayerWeaponClipV3(string animSet, string slot) =>
            "AP022_tp_" + (System.Array.IndexOf(WeaponOwnSlots, slot) >= 0 ? animSet : WeaponClassOf(animSet)) + "_" + slot;
        /// <summary>WP-071 B4b-3 · AP-022 slice 5: the FP viewmodel twin of a v3 slot (same class / weapon split).</summary>
        public static string PlayerFpClipV3(string animSet, string slot) =>
            "AP022_fp_" + (System.Array.IndexOf(WeaponOwnSlots, slot) >= 0 ? animSet : WeaponClassOf(animSet)) + "_" + slot;
        public const string FpWalkSway = "AP022_fp_walk_sway", FpRunSway = "AP022_fp_run_sway";
        public const string FpBaseLayer = "fp_base", FpAdditiveLayer = "fp_additive", FpSwayLayer = "fp_sway", FpRun = "FpRun", FpSwayState = "Sway";
        public static string PlayerWeaponClip(int weapon, string kind) => "AP009_player_" + WeaponLetters[Mathf.Clamp(weapon, 0, 2)] + "_" + kind;   // idle_aim / fire / reload / switch_in / switch_out
        public const string ZombieIdleSway = "AP009_zombie_idle_sway";
        public const string ZombieIdleTwitch = "AP009_zombie_idle_twitch";
        public const string ZombieIdleQueue = "AP009_zombie_idle_queue";   // WP-032B (v002)
        public const string ZombieShambleA = "AP009_zombie_shamble_A";
        public const string ZombieShambleB = "AP009_zombie_shamble_B";
        public const string ZombieShambleC = "AP009_zombie_shamble_C";
        public const string ZombieRun = "AP009_zombie_run";
        public const string ZombieAttackLunge = "AP009_zombie_attack_lunge";
        public const string ZombieAttackSweep = "AP009_zombie_attack_sweep";
        public const string ZombieAttackSwipe = "AP009_zombie_attack_swipe";
        public const string ZombieAttackOverhead = "AP009_zombie_attack_overhead";   // WP-032B (v002, Ultra)
        public const string ZombieHitLightL = "AP009_zombie_hit_light_L";
        public const string ZombieHitLightR = "AP009_zombie_hit_light_R";
        public const string ZombieHitHeavyStagger = "AP009_zombie_hit_heavy_stagger";
        public const string ZombieHitRegionHead = "AP009_zombie_hit_region_head";
        public const string ZombieHitRegionTorso = "AP009_zombie_hit_region_torso";
        public const string ZombieHitRegionLeg = "AP009_zombie_hit_region_leg";
        public const string ZombieDeathForward = "AP009_zombie_death_forward";
        public const string ZombieDeathBackward = "AP009_zombie_death_backward";
        public const string ZombieDeathSide = "AP009_zombie_death_side";
        public const string ZombieDeathKneel = "AP009_zombie_death_kneel";
        public const string ZombieRise = "AP009_zombie_rise";
        // WP-032C · AP-009C v003
        public const string ZombieRiseB = "AP009_zombie_rise_B";
        public const string ZombieRiseC = "AP009_zombie_rise_C";
        public const string ZombieDeathSideR = "AP009_zombie_death_side_R";
        public const string ZombieDeathSpecialA = "AP009_zombie_death_special_A";
        public const string ZombieDeathSpecialB = "AP009_zombie_death_special_B";
        public const string ZombieDeathUltra = "AP009_zombie_death_ultra";
        public const string ZombieHitSpecialStumble = "AP009_zombie_hit_special_stumble";
        // WP-071 · AP-022 slice 1 (v008): the four death onsets (full falls with ragdoll_handoff) and the five attacks (+ swipe_L)
        public const string ZombieDeathFallFwdV3 = "AP022_zombie_death_fall_fwd";
        public const string ZombieDeathFallBackV3 = "AP022_zombie_death_fall_back";
        public const string ZombieDeathSpinSideV3 = "AP022_zombie_death_spin_side";
        public const string ZombieDeathLegsOutV3 = "AP022_zombie_death_legs_out";
        public const string ZombieAttackSwipeRV3 = "AP022_zombie_attack_swipe_R";
        public const string ZombieAttackSwipeLV3 = "AP022_zombie_attack_swipe_L";
        public const string ZombieAttackLungeV3 = "AP022_zombie_attack_lunge";
        public const string ZombieAttackSweepV3 = "AP022_zombie_attack_sweep";
        public const string ZombieAttackOverheadSlamV3 = "AP022_zombie_attack_overhead_slam";
        public const string ZombieAttackBiteGrabV3 = "AP022_zombie_attack_bite_grab";
        /// <summary>WP-071: AP-022 clips in DeathStyle order where they exist (0 fwd, 1 back, 2 side, 3 kneel → legs_out, 4 side_R = spin_side mirrored); 5–7 stay AP-009 until later slices.</summary>
        public static readonly string[] ZombieDeathClipsV3 = { ZombieDeathFallFwdV3, ZombieDeathFallBackV3, ZombieDeathSpinSideV3, ZombieDeathLegsOutV3, ZombieDeathSpinSideV3, "AP009_zombie_death_special_A", "AP009_zombie_death_special_B", "AP009_zombie_death_ultra" };
        // WP-071 · AP-022 slice 2a (v008): the Special reactions — Raiju seize loop + its death, Namekuji's lethal slip
        public const string ZombieHitElectroLoopV3 = "AP022_zombie_hit_electro_loop";
        public const string ZombieDeathArcElectroV3 = "AP022_zombie_death_arc_electro";
        public const string ZombieHitGelSlipV3 = "AP022_zombie_hit_gel_slip";
        /// <summary>WP-071: DeathStyle 8 / 9 — the Special deaths (Death_8 arc_electro, Death_9 gel_slip), chosen by the visual from the body state, never by the gameplay index.</summary>
        public const int DeathStyleArcElectro = 8, DeathStyleGelSlip = 9;
        public static readonly string[] ZombieSpecialDeathClipsV3 = { ZombieDeathArcElectroV3, ZombieHitGelSlipV3 };
        /// <summary>WP-071: AP-022 attacks by state index (0 swipe_R, 1 lunge, 2 sweep, 3 overhead_slam, 4 bite_grab, 5 swipe_L).</summary>
        public static readonly string[] ZombieAttackClipsV3 = { ZombieAttackSwipeRV3, ZombieAttackLungeV3, ZombieAttackSweepV3, ZombieAttackOverheadSlamV3, ZombieAttackBiteGrabV3, ZombieAttackSwipeLV3 };
        public static readonly string[] HitDirections = { "front", "back", "left", "right" };
        public static readonly string[] HitTiers = { "std", "elite", "ultra" };
        public static readonly string[] ArmorBreakRegions = { "head", "torso", "leg" };
        public static string ZombieHitClip(int dir, int tier) => "AP009_zombie_hit_" + HitDirections[Mathf.Clamp(dir, 0, 3)] + "_" + HitTiers[Mathf.Clamp(tier, 0, 2)];
        public static string ZombieArmorBreakClip(int region) => "AP009_zombie_armorbreak_" + ArmorBreakRegions[Mathf.Clamp(region, 0, 2)];

        public static readonly string[] PlayerClips =
        {
            PlayerIdle, PlayerWalkFwd, PlayerWalkBack, PlayerStrafeLeft, PlayerStrafeRight,
            PlayerRunFwd, PlayerRunBack, PlayerRunStrafeLeft, PlayerRunStrafeRight,
            PlayerFireRecoil, PlayerHitLight, PlayerDeathFwd, PlayerDeathBack, PlayerPickup,
            PlayerPickupUpper, PlayerSwHoldBell, PlayerSwHoldLoom, PlayerSwHoldJewel, PlayerDeathSide, PlayerHurtA, PlayerHurtB, PlayerWeaponLower, PlayerWeaponRaise,
            "AP009_player_B_weapon_lower", "AP009_player_B_weapon_raise", "AP009_player_C_weapon_lower", "AP009_player_C_weapon_raise",
            "AP009_player_A_idle_aim", "AP009_player_A_fire", "AP009_player_A_reload", "AP009_player_A_switch_in", "AP009_player_A_switch_out",
            "AP009_player_B_idle_aim", "AP009_player_B_fire", "AP009_player_B_reload", "AP009_player_B_switch_in", "AP009_player_B_switch_out",
            "AP009_player_C_idle_aim", "AP009_player_C_fire", "AP009_player_C_reload", "AP009_player_C_switch_in", "AP009_player_C_switch_out",
            // WP-071 AP-022 slices 4a/4b (30 fps): AR + SMG class sets, ar_1 / ar_2 / smg_1 weapon slots (+ ADS: in / idle / out)
            "AP022_tp_ar_idle_aim", "AP022_tp_ar_ads_in", "AP022_tp_ar_ads_idle", "AP022_tp_ar_ads_out", "AP022_tp_ar_switch_in", "AP022_tp_ar_switch_out",
            "AP022_tp_ar_1_fire", "AP022_tp_ar_1_reload_tac", "AP022_tp_ar_1_reload_empty", "AP022_tp_ar_2_fire", "AP022_tp_ar_2_reload_tac", "AP022_tp_ar_2_reload_empty",
            "AP022_tp_smg_idle_aim", "AP022_tp_smg_ads_in", "AP022_tp_smg_ads_idle", "AP022_tp_smg_ads_out", "AP022_tp_smg_switch_in", "AP022_tp_smg_switch_out",
            "AP022_tp_smg_1_fire", "AP022_tp_smg_1_reload_tac", "AP022_tp_smg_1_reload_empty"
        };

        public static readonly string[] ZombieClips =
        {
            ZombieIdleSway, ZombieIdleTwitch, ZombieIdleQueue, ZombieShambleA, ZombieShambleB, ZombieShambleC, ZombieRun,
            ZombieAttackLunge, ZombieAttackSweep, ZombieAttackSwipe, ZombieAttackOverhead,
            ZombieHitLightL, ZombieHitLightR, ZombieHitHeavyStagger,
            ZombieHitRegionHead, ZombieHitRegionTorso, ZombieHitRegionLeg,
            ZombieDeathForward, ZombieDeathBackward, ZombieDeathSide, ZombieDeathKneel, ZombieRise,
            ZombieRiseB, ZombieRiseC, ZombieDeathSideR, ZombieDeathSpecialA, ZombieDeathSpecialB, ZombieDeathUltra, ZombieHitSpecialStumble,
            "AP009_zombie_armorbreak_head", "AP009_zombie_armorbreak_torso", "AP009_zombie_armorbreak_leg",
            "AP009_zombie_hit_front_std", "AP009_zombie_hit_back_std", "AP009_zombie_hit_left_std", "AP009_zombie_hit_right_std",
            "AP009_zombie_hit_front_elite", "AP009_zombie_hit_back_elite", "AP009_zombie_hit_left_elite", "AP009_zombie_hit_right_elite",
            "AP009_zombie_hit_front_ultra", "AP009_zombie_hit_back_ultra", "AP009_zombie_hit_left_ultra", "AP009_zombie_hit_right_ultra",
            // WP-071 AP-022 slice 1
            ZombieDeathFallFwdV3, ZombieDeathFallBackV3, ZombieDeathSpinSideV3, ZombieDeathLegsOutV3,
            ZombieAttackSwipeRV3, ZombieAttackSwipeLV3, ZombieAttackLungeV3, ZombieAttackSweepV3, ZombieAttackOverheadSlamV3, ZombieAttackBiteGrabV3,
            // WP-071 AP-022 slice 2a
            ZombieHitElectroLoopV3, ZombieDeathArcElectroV3, ZombieHitGelSlipV3
        };

        public const int ClipCount = 182;   // WP-071: + B4c TP 33 (sniper 11, launcher 8, LMG 11, smg_2 3); + AP-022 slice 1 (4 deaths + 6 attacks), slice 2a (electro loop, arc_electro death, gel_slip), slices 4a/4b (AR + SMG TP 21), slice 5/5b (FP arms 29 on fpView).   // WP-032G: + sw_hold_jewel (AP-009G v007). WP-032C: AP-009D v004 (v002 36 + 49: weapon A/B/C ×5, lower/raise ×3, sw hold ×2, pickup_upper, hurt ×2, death_side, tier hits ×12, armour breaks ×3, stumble, deaths ×4, rises ×2)
        public const int Fps = 30;

        /// <summary>The zombie death clips in DeathStyle order (0 fwd, 1 back, 2 side, 3 kneel; WP-032C: 4 side_R, 5 special_A, 6 special_B, 7 ultra).</summary>
        public static readonly string[] ZombieDeathClips = { ZombieDeathForward, ZombieDeathBackward, ZombieDeathSide, ZombieDeathKneel, ZombieDeathSideR, ZombieDeathSpecialA, ZombieDeathSpecialB, ZombieDeathUltra };
        /// <summary>The zombie rise clips in RiseVariant order (0 rise, 1 rise_B, 2 rise_C — B/C are camera-off-only, ≤ 1.2 s).</summary>
        public static readonly string[] ZombieRiseClips = { ZombieRise, ZombieRiseB, ZombieRiseC };
        /// <summary>The player death clips in DeathStyle order (0 fwd, 1 back, 2 side).</summary>
        public static readonly string[] PlayerDeathClips = { PlayerDeathFwd, PlayerDeathBack, PlayerDeathSide };
        /// <summary>The zombie attack clips in AttackStyle order (0 swipe, 1 lunge, 2 sweep, 3 overhead).</summary>
        public static readonly string[] ZombieAttackClips = { ZombieAttackSwipe, ZombieAttackLunge, ZombieAttackSweep, ZombieAttackOverhead };
        /// <summary>The zombie gait clips in Gait order (0 A, 1 B, 2 C, 3 run).</summary>
        public static readonly string[] ZombieGaitClips = { ZombieShambleA, ZombieShambleB, ZombieShambleC, ZombieRun };
    }

    /// <summary>
    /// WP-016/018 · The clips a character's Animator plays, as one asset: the WP-016 rig-test
    /// pair (idle / walk, 1-D blend on <c>Speed</c>) stays as the fallback when AP-009 is
    /// absent, and the WP-018 table carries the AP-009 clips (36 with AP-009B v002) with their manifest data
    /// (loop, seconds, no-slide ground speed) for the generated controller
    /// (<c>ArtAnimatorBuilder</c>) and the runtime rate maths (<see cref="SpeedOf"/>).
    /// </summary>
    public sealed class CharacterAnimationSet : ScriptableObject
    {
        public const string SpeedParameter = CharacterAnimatorContract.Speed;
        public const string LocomotionState = CharacterAnimatorContract.LocomotionState;

        [Serializable]
        public sealed class Entry
        {
            public string name;
            public AnimationClip clip;
            public bool loop;
            public float seconds;
            /// <summary>Manifest speed_mps: ground speed at playback rate 1 with no foot slide (0 = not locomotion).</summary>
            public float speedMps;
            /// <summary>WP-032: the same speed measured on the Unity rig (StrideCalibrator; 0 = not calibrated → manifest value).</summary>
            public float unitySpeedMps;
            public string layer;
            public bool additive;
            public int frames;
            /// <summary>Manifest events as "name=frame;…" (provenance / tests).</summary>
            public string events;
            /// <summary>WP-032C: manifest stance frames per foot (contact length of a locomotion cycle; 0 = not given). The calibrator and the slip tests measure over exactly these frames.</summary>
            public int stanceFrames;
        }

        [SerializeField] private AnimationClip _idle;
        [SerializeField] private AnimationClip _walk;
        [Tooltip("Ground speed at which the rig-test walk clip plays at 1x (Speed parameter 1).")]
        [SerializeField] private float _walkSpeedMetersPerSecond = 1.4f;
        [SerializeField] private RuntimeAnimatorController _controller;
        [Tooltip("Provenance: which AP delivered the clips.")]
        [SerializeField] private string _source = "AP-006 rig tests (RigTest_Idle / RigTest_Walk); AP-009 replaces";
        [Tooltip("WP-018 · AP-009 clip table (empty = rig-test set).")]
        [SerializeField] private List<Entry> _clips = new List<Entry>();
        [Tooltip("WP-018 · AP-009 manifest schema the table was built from.")]
        [SerializeField] private string _manifestSchema = "";
        [Tooltip("WP-018 · Ground speed (m/s) the 2-D locomotion blend is normalised to (player: config MoveSpeed at build; the child time scales are reference / clip speed_mps).")]
        [SerializeField] private float _locomotionReferenceSpeed = 6.2f;
        [Tooltip("WP-071 B4b (K8): per-weapon override controllers keyed by WeaponDefinition.AnimSet (v3 weapon clips); empty = legacy pose path only.")]
        [SerializeField] private List<WeaponAnimSet> _weaponSets = new List<WeaponAnimSet>();

        [Serializable]
        public sealed class WeaponAnimSet
        {
            public string animSet;
            public AnimatorOverrideController overrides;
            /// <summary>WP-071 B4b: the class rest pose's left hand in the weapon frame (AP-022 hands_in_weapon_frame) — the left-hand IK target.</summary>
            public bool hasLeftHand;
            public Vector3 leftHandInWeapon;
            /// <summary>WP-071 B4b-3: the socket bone's axes in the weapon frame (AP-022 hands_in_weapon_frame) — the FP weapon frame = socket · inverse(this).</summary>
            public bool hasSocketRot;
            public Quaternion socketRotInWeapon = Quaternion.identity;
            /// <summary>WP-071 B4b-3: P-06's constant fp_camera offset of this weapon's FP clips (rig space: x right, z look) — Humanoid clips do not carry the extra-transform key, so it is data.</summary>
            public Vector3 fpCameraOffset;
            /// <summary>WP-071 B4c: the set has a bolt cycle (Cycle_V3 after each shot).</summary>
            public bool hasCycle;
        }

        public IReadOnlyList<WeaponAnimSet> WeaponSets => _weaponSets;

        [Tooltip("WP-071 B4b-3: the FP viewmodel controller (AP-022 slice 5 on the same PlayerRig; fp_camera = the viewmodel anchor) and its per-weapon override controllers.")]
        [SerializeField] private RuntimeAnimatorController _fpController;
        [SerializeField] private List<WeaponAnimSet> _fpWeaponSets = new List<WeaponAnimSet>();
        public RuntimeAnimatorController FpController => _fpController;
        public IReadOnlyList<WeaponAnimSet> FpWeaponSets => _fpWeaponSets;

        /// <summary>Editor (builder): the FP viewmodel controller + sets.</summary>
        public void ConfigureFirstPerson(RuntimeAnimatorController controller, List<WeaponAnimSet> sets)
        {
            _fpController = controller;
            _fpWeaponSets = sets ?? new List<WeaponAnimSet>();
        }

        /// <summary>WP-071 B4b: the override controller for a weapon's AnimSet key, or null (legacy pose path).</summary>
        public AnimatorOverrideController WeaponOverrides(string animSet)
        {
            if (_weaponSets == null || string.IsNullOrEmpty(animSet)) return null;
            for (int i = 0; i < _weaponSets.Count; i++) if (_weaponSets[i] != null && _weaponSets[i].animSet == animSet) return _weaponSets[i].overrides;
            return null;
        }

        /// <summary>Editor (builder): the v3 weapon sets.</summary>
        public void ConfigureWeaponSets(List<WeaponAnimSet> sets) => _weaponSets = sets ?? new List<WeaponAnimSet>();

        public AnimationClip Idle => _idle;
        public AnimationClip Walk => _walk;
        public float WalkSpeedMetersPerSecond => Mathf.Max(0.1f, _walkSpeedMetersPerSecond);
        public RuntimeAnimatorController Controller => _controller;
        public string Source => _source;
        public IReadOnlyList<Entry> Clips => _clips;
        public string ManifestSchema => _manifestSchema;
        public float LocomotionReferenceSpeed => Mathf.Max(0.5f, _locomotionReferenceSpeed);

        /// <summary>True when the AP-009 table is populated (the generated Animator has the WP-018 layers/parameters).</summary>
        public bool IsAuthored => _clips != null && _clips.Count > 0;

        /// <summary>Speed parameter for a ground speed (rig-test blend: 0 idle … 1 walk clip speed, clamped at 2).</summary>
        public float SpeedParameterFor(float metersPerSecond)
        {
            return Mathf.Clamp(metersPerSecond / WalkSpeedMetersPerSecond, 0f, 2f);
        }

        public Entry Find(string clipName)
        {
            if (_clips == null || clipName == null) return null;
            for (int i = 0; i < _clips.Count; i++)
            {
                if (_clips[i] != null && _clips[i].name == clipName) return _clips[i];
            }

            return null;
        }

        public AnimationClip Clip(string clipName)
        {
            Entry e = Find(clipName);
            return e != null ? e.clip : null;
        }

        /// <summary>No-slide ground speed of a locomotion clip on the Unity rig (WP-032 calibration; manifest value when uncalibrated; 0 when unknown).</summary>
        public float SpeedOf(string clipName)
        {
            Entry e = Find(clipName);
            if (e == null) return 0f;
            return e.unitySpeedMps > 0.01f ? e.unitySpeedMps : Mathf.Max(0f, e.speedMps);
        }

        /// <summary>The manifest (Blender) speed, for the calibration report.</summary>
        public float ManifestSpeedOf(string clipName)
        {
            Entry e = Find(clipName);
            return e != null ? Mathf.Max(0f, e.speedMps) : 0f;
        }

        /// <summary>Playback rate that shows no foot slide at <paramref name="metersPerSecond"/> (1 when the clip speed is unknown).</summary>
        public float RateFor(string clipName, float metersPerSecond)
        {
            float clipSpeed = SpeedOf(clipName);
            return clipSpeed > 0.01f ? Mathf.Max(0f, metersPerSecond) / clipSpeed : 1f;
        }

        /// <summary>Editor (builder): rewrite the table.</summary>
        public void Configure(AnimationClip idle, AnimationClip walk, float walkSpeed, RuntimeAnimatorController controller, string source, List<Entry> clips, string manifestSchema, float locomotionReferenceSpeed)
        {
            _locomotionReferenceSpeed = locomotionReferenceSpeed;
            _idle = idle;
            _walk = walk;
            _walkSpeedMetersPerSecond = walkSpeed;
            _controller = controller;
            _source = source;
            _clips = clips ?? new List<Entry>();
            _manifestSchema = manifestSchema ?? "";
        }
    }
}
