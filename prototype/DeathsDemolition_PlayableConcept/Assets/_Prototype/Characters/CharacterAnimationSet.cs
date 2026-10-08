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
        /// <summary>Zombie: gait tree child index = <see cref="ZombieGaitClips"/> index (0–3 AP-009, 4–8 AP-022, 9–13 AP-033; per-spawn tier pool pick).</summary>
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
        public const string AdsWeight = "AdsWeight";     // float: WP-071 r2 — the TP v3 aim-down-sights weight (gameplay ClassicWeaponRuntime.Ads01)
        public const string FireHeld = "FireHeld";       // bool: WP-071 r2 — a sustained string of shots (the LMG fire loop)
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
        // WP-082 R3 (LOCO-SOZLESME §1): with the AP-026 8-way tree (LocoSpeed present) MoveX / MoveY are the body-space UNIT
        // direction (hip hysteresis + angle smoothing) and LocoRate = clamp(v / honest speed, CadenceBand); the AP-022 4-way
        // controller (no LocoSpeed) keeps the old meaning: local velocity / reference speed, LocoRate = |v| / reference.
        public const string MoveX = "MoveX";             // player: body-space direction x (right +)
        public const string MoveY = "MoveY";             // player: body-space direction y (forward +)
        public const string LocoRate = "LocoRate";       // player: Locomotion state speed multiplier
        /// <summary>WP-082 R3: float, the planar speed in m/s (hero world, unsmoothed) — the 1-D ring tree's parameter.</summary>
        public const string LocoSpeed = "LocoSpeed";
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
            SwitchInV3State = "SwitchIn_V3", SwitchOutV3State = "SwitchOut_V3", FireV3State = "Fire_V3", CycleV3State = "Cycle_V3",
            CycleAdsV3State = "CycleAds_V3", FireLoopV3State = "FireLoop_V3";
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
        // WP-071 (C-00 01.10, P-06 AP-022 v008 player locomotion): the locomotion contract names are the AP-022 TP loops — walk ring
        // 20 f (fwd 2.6 / back 2.55 / strafes 2.1 m/s contact spd), run ring (5.8 / 3.6 / 4.6), idle 120 f; the AP-009 loops stay in the set unbound.
        public const string PlayerIdle = "AP022_tp_idle";
        public const string PlayerWalkFwd = "AP022_tp_walk_fwd";
        public const string PlayerWalkBack = "AP022_tp_walk_back";
        public const string PlayerStrafeLeft = "AP022_tp_walk_strafe_l";
        public const string PlayerStrafeRight = "AP022_tp_walk_strafe_r";
        /// <summary>WP-071: AP-022 walk_slow (30 f, 1.3 m/s) — imported, not in the tree in R1 (a forward-only inner ring would bend the direction blend).</summary>
        public const string PlayerWalkSlow = "AP022_tp_walk_slow";
        /// <summary>WP-071 MUST-3: the generated static stand (PlayerIdle frame 0 held) at the locomotion tree's centre — the breath plays in its own state.</summary>
        public const string PlayerStandStill = "DD_Player_StandStill";
        /// <summary>WP-071 MUST-3: bool — the body has stood (|v| &lt; 0.1 m/s) for 0.2 s → the base layer plays the idle breath in <see cref="StillState"/>.</summary>
        public const string Still = "Still";
        public const string StillState = "Idle_Breath";
        // WP-032B · AP-009B v002 run cycles (flight phase; the outer ring of the 2-D locomotion blend)
        public const string PlayerRunFwd = "AP022_tp_run_fwd";
        public const string PlayerRunBack = "AP022_tp_run_back";
        public const string PlayerRunStrafeLeft = "AP022_tp_run_strafe_l";
        public const string PlayerRunStrafeRight = "AP022_tp_run_strafe_r";
        // WP-082 R3 (LOCO-SOZLESME §2.3, PLAN §4.2): the AP-026 v002 mixamo-hero loops, 8 directions per ring in
        // LocomotionV4.Directions order (0 fwd, 1 fwd_r, 2 right, 3 back_r, 4 back, 5 back_l, 6 left, 7 fwd_l); take = clip name
        public const string PlayerIdleAp026 = "AP026_tp_idle_03";
        public static readonly string[] PlayerWalk = Ring8("walk");
        public static readonly string[] PlayerRun = Ring8("run");
        public static readonly string[] PlayerSprint = Ring8("sprint");
        private static string[] Ring8(string ring)
        {
            var n = new string[8];
            for (int i = 0; i < 8; i++) n[i] = LocomotionV4.ClipPrefix + ring + "_" + LocomotionV4.Directions[i];
            return n;
        }
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
        public static readonly string[] WeaponAnimSetsV3 = { "ar_1", "ar_2", "smg_1", "smg_2", "sr_1", "rl_1", "lmg_1", "lmg_2", "sw_arc", "sw_gel" };   // WP-071: + TP Special family (opt-in; right-grip MUST open)
        /// <summary>The class of a set (its class clips' prefix): the weapon id's prefix, AP-022's class names for the sniper (snp) and launcher (lch).</summary>
        public static string WeaponClassOf(string animSet)
        {
            int u = animSet.IndexOf('_');
            string c = u > 0 ? animSet.Substring(0, u) : animSet;
            return c == "sr" ? "snp" : c == "rl" ? "lch" : c;
        }

        /// <summary>WP-071 B4c: a set's bolt cycle after a shot (AP022_tp_&lt;set&gt;_bolt_cycle — sr_1).</summary>
        public static string PlayerWeaponCycleClipV3(string animSet) => "AP022_tp_" + animSet + "_bolt_cycle";
        /// <summary>WP-071 r2: the bolt cycle from the ADS pose (AP022_tp_&lt;set&gt;_bolt_cycle_ads — sr_1).</summary>
        public static string PlayerWeaponCycleAdsClipV3(string animSet) => "AP022_tp_" + animSet + "_bolt_cycle_ads";
        /// <summary>WP-071 r2: the class's sustained-fire loop (AP022_tp_&lt;class&gt;_fire_loop — lmg).</summary>
        public static string PlayerWeaponFireLoopClipV3(string animSet) => "AP022_tp_" + WeaponClassOf(animSet) + "_fire_loop";
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
        // WP-071 · AP-022 zombie locomotion (v008, P-06 AD MUST-I1): each tier's own gait — Standard shamble_fast A / B (2.4 m/s),
        // Runner run (5.3), Guard walk_heavy (2.1), Executioner walk_stomp (1.7); in-place, foot_l / foot_r + foot_*_off
        public const string ZombieShambleFastAV3 = "AP022_zombie_shamble_fast_A";
        public const string ZombieShambleFastBV3 = "AP022_zombie_shamble_fast_B";
        public const string ZombieRunV3 = "AP022_zombie_run";
        public const string ZombieWalkHeavyV3 = "AP022_zombie_walk_heavy";
        public const string ZombieWalkStompV3 = "AP022_zombie_walk_stomp";
        /// <summary>WP-071 MUST-I1: Gait indices of the AP-022 tier gaits in <see cref="ZombieGaitClips"/> (0–3 stay the AP-009 fallback).</summary>
        public const int GaitShambleFastAV3 = 4, GaitShambleFastBV3 = 5, GaitRunV3 = 6, GaitWalkHeavyV3 = 7, GaitWalkStompV3 = 8;
        // WP-096 · AP-033 p1 (P-06 p1-v001, 73-bone ZombieRig, in place, 30 fps): native speed = the tier's world speed / its body
        // scale (spd, honest rate ≈ 1.0): Standard lurch_A / limp_A 2.7, Runner run_feral_A 4.79, Executioner walk_stomp_A 1.15
        public const string ZombieWalkLurchAV4 = "AP033_zombie_walk_lurch_A";
        public const string ZombieWalkLimpAV4 = "AP033_zombie_walk_limp_A";
        public const string ZombieRunFeralAV4 = "AP033_zombie_run_feral_A";
        public const string ZombieWalkStompAV4 = "AP033_zombie_walk_stomp_A";
        /// <summary>
        /// WP-096 AP-033 p1: Gait indices appended after the AP-022 ones (0–8 keep their numbers). 11 = walk_limp_A played MIRRORED
        /// (limp R; the gait tree child carries ChildMotion.mirror, the clip is the same) — see <see cref="IsGaitMirrored"/>.
        /// </summary>
        public const int GaitWalkLurchAV4 = 9, GaitWalkLimpAV4 = 10, GaitWalkLimpRV4 = 11, GaitRunFeralAV4 = 12, GaitWalkStompAV4 = 13;
        // WP-096 · AP-033 p2 (P-06 p2-v001): the remaining loop variants — Standard lurch_B / drag_A / drag_B 2.7, Runner run_feral_B /
        // run_stumble_A 4.79, Guard walk_heavy_A / B 1.9 (2.2 / 1.16), Executioner walk_stomp_B 1.15 (Blender spd; the set carries the
        // Unity-rig speed the intake measures, ArtZombieStrideCalibration)
        public const string ZombieWalkLurchBV4 = "AP033_zombie_walk_lurch_B";
        public const string ZombieWalkDragAV4 = "AP033_zombie_walk_drag_A";
        public const string ZombieWalkDragBV4 = "AP033_zombie_walk_drag_B";
        public const string ZombieRunFeralBV4 = "AP033_zombie_run_feral_B";
        public const string ZombieRunStumbleAV4 = "AP033_zombie_run_stumble_A";
        public const string ZombieWalkHeavyAV4 = "AP033_zombie_walk_heavy_A";
        public const string ZombieWalkHeavyBV4 = "AP033_zombie_walk_heavy_B";
        public const string ZombieWalkStompBV4 = "AP033_zombie_walk_stomp_B";
        /// <summary>WP-096 AP-033 p2: Gait indices 14–21 (0–13 keep their numbers).</summary>
        public const int GaitWalkLurchBV4 = 14, GaitWalkDragAV4 = 15, GaitWalkDragBV4 = 16, GaitRunFeralBV4 = 17, GaitRunStumbleAV4 = 18,
            GaitWalkHeavyAV4 = 19, GaitWalkHeavyBV4 = 20, GaitWalkStompBV4 = 21;
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
            PlayerRunFwd, PlayerRunBack, PlayerRunStrafeLeft, PlayerRunStrafeRight, PlayerWalkSlow,
            // the AP-009 locomotion loops (unbound since the AP-022 contract names, WP-071 01.10)
            "AP009_player_idle", "AP009_player_walk_fwd", "AP009_player_walk_back", "AP009_player_strafe_left", "AP009_player_strafe_right",
            "AP009_player_run_fwd", "AP009_player_run_back", "AP009_player_run_strafe_left", "AP009_player_run_strafe_right",
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
            ZombieHitElectroLoopV3, ZombieDeathArcElectroV3, ZombieHitGelSlipV3,
            // WP-071 AP-022 zombie locomotion (MUST-I1)
            ZombieShambleFastAV3, ZombieShambleFastBV3, ZombieRunV3, ZombieWalkHeavyV3, ZombieWalkStompV3,
            // WP-096 AP-033 p1 zombie locomotion
            ZombieWalkLurchAV4, ZombieWalkLimpAV4, ZombieRunFeralAV4, ZombieWalkStompAV4,
            // WP-096 AP-033 p2
            ZombieWalkLurchBV4, ZombieWalkDragAV4, ZombieWalkDragBV4, ZombieRunFeralBV4, ZombieRunStumbleAV4, ZombieWalkHeavyAV4, ZombieWalkHeavyBV4, ZombieWalkStompBV4,
            // WP-096 AP-033 p3 transitions (never added to ZombieGaitClips)
            "AP033_zombie_start_walk", "AP033_zombie_start_run", "AP033_zombie_stop_walk_l", "AP033_zombie_stop_walk_r",
            "AP033_zombie_stop_run_l", "AP033_zombie_stop_run_r", "AP033_zombie_turn90_L", "AP033_zombie_turn180_L",
            "AP033_zombie_turn45_run_L", "AP033_zombie_stumble_recover"
        };

        public const int ClipCount = 254;   // WP-096: + AP-033 p3 transitions 10; + AP-033 p2 zombie locomotion 8 (lurch_B, drag_A/B, feral_B, stumble_A, heavy_A/B, stomp_B); + AP-033 p1 zombie locomotion 4 (lurch_A, limp_A, run_feral_A, walk_stomp_A); + AP-022 player locomotion 10 (MUST-3/4); + AP-022 zombie locomotion 5 (MUST-I1); + FP Special 12 (sw_arc / sw_gel); + lmg_2 tray reloads 2; + FP arms batch smg_2 FP 9 (WP-071 r2); + TP Special family 12; WP-071: + B4c TP 33 (sniper 11, launcher 8, LMG 11, smg_2 3); + AP-022 slice 1 (4 deaths + 6 attacks), slice 2a (electro loop, arc_electro death, gel_slip), slices 4a/4b (AR + SMG TP 21), slice 5/5b (FP arms 29 on fpView).   // WP-032G: + sw_hold_jewel (AP-009G v007). WP-032C: AP-009D v004 (v002 36 + 49: weapon A/B/C ×5, lower/raise ×3, sw hold ×2, pickup_upper, hurt ×2, death_side, tier hits ×12, armour breaks ×3, stumble, deaths ×4, rises ×2)
        public const int Fps = 30;

        /// <summary>The zombie death clips in DeathStyle order (0 fwd, 1 back, 2 side, 3 kneel; WP-032C: 4 side_R, 5 special_A, 6 special_B, 7 ultra).</summary>
        public static readonly string[] ZombieDeathClips = { ZombieDeathForward, ZombieDeathBackward, ZombieDeathSide, ZombieDeathKneel, ZombieDeathSideR, ZombieDeathSpecialA, ZombieDeathSpecialB, ZombieDeathUltra };
        /// <summary>The zombie rise clips in RiseVariant order (0 rise, 1 rise_B, 2 rise_C — B/C are camera-off-only, ≤ 1.2 s).</summary>
        public static readonly string[] ZombieRiseClips = { ZombieRise, ZombieRiseB, ZombieRiseC };
        /// <summary>The player death clips in DeathStyle order (0 fwd, 1 back, 2 side).</summary>
        public static readonly string[] PlayerDeathClips = { PlayerDeathFwd, PlayerDeathBack, PlayerDeathSide };
        /// <summary>The zombie attack clips in AttackStyle order (0 swipe, 1 lunge, 2 sweep, 3 overhead).</summary>
        public static readonly string[] ZombieAttackClips = { ZombieAttackSwipe, ZombieAttackLunge, ZombieAttackSweep, ZombieAttackOverhead };
        /// <summary>
        /// The zombie gait clips in Gait order (0 A, 1 B, 2 C, 3 run — AP-009; WP-071 MUST-I1: 4 shamble_fast_A, 5 shamble_fast_B, 6 run, 7 walk_heavy,
        /// 8 walk_stomp — AP-022; WP-096: 9 lurch_A, 10 limp_A, 11 limp_A mirrored (limp R), 12 run_feral_A, 13 walk_stomp_A — AP-033 p1;
        /// 14 lurch_B, 15 drag_A, 16 drag_B, 17 run_feral_B, 18 run_stumble_A, 19 walk_heavy_A, 20 walk_heavy_B, 21 walk_stomp_B — AP-033 p2).
        /// An index is a gait tree child; the same clip may sit at two indices (10 / 11 differ only by the child's mirror flag).
        /// </summary>
        public static readonly string[] ZombieGaitClips =
        {
            ZombieShambleA, ZombieShambleB, ZombieShambleC, ZombieRun, ZombieShambleFastAV3, ZombieShambleFastBV3, ZombieRunV3, ZombieWalkHeavyV3, ZombieWalkStompV3,
            ZombieWalkLurchAV4, ZombieWalkLimpAV4, ZombieWalkLimpAV4, ZombieRunFeralAV4, ZombieWalkStompAV4,
            ZombieWalkLurchBV4, ZombieWalkDragAV4, ZombieWalkDragBV4, ZombieRunFeralBV4, ZombieRunStumbleAV4, ZombieWalkHeavyAV4, ZombieWalkHeavyBV4, ZombieWalkStompBV4
        };

        /// <summary>WP-096 AP-033 p1: the gait tree child at this index plays its clip mirrored (limp R = limp_A through Unity's humanoid mirror).</summary>
        public static bool IsGaitMirrored(int gait) => gait == GaitWalkLimpRV4;

        /// <summary>
        /// WP-096 AP-033 · the per-tier gait pools, indexed by <c>(int)EnemyTier</c> (0 Standard, 1 Special/Runner, 2 Elite/Guard,
        /// 3 Ultra/Executioner). Entry 0 is the tier's primary look (the pick while the per-spawn variety is off); a spawn walks the
        /// pool in order (ZombieSkinnedVisual.GaitIndexFor), skipping members the set does not carry.
        /// p2 (S-03 08.10): Standard 6 AP-033 looks (lurch A/B, limp L/R, drag A/B; the limp sides and the A/B pairs apart so
        /// consecutive spawns differ in silhouette) — AP-022 shamble_fast A/B leave the pool (150–157 spm at ×1.125 read frantic);
        /// Runner feral A/B + stumble — AP-022 run leaves (rate 0.90, a different arm read); Guard heavy A/B + AP-022 walk_heavy
        /// (calibrated, heavy read, a third look for the six-body Guard share); Executioner stomp A/B — AP-022 walk_stomp leaves
        /// (rate 0.68 under its own 0.6 floor). The dropped AP-022 gaits keep their indices and are <see cref="ZombieGaitFallbacks"/>.
        /// </summary>
        public static readonly int[][] ZombieGaitPools =
        {
            new[] { GaitWalkLurchAV4, GaitWalkLimpAV4, GaitWalkDragAV4, GaitWalkLurchBV4, GaitWalkLimpRV4, GaitWalkDragBV4 },
            new[] { GaitRunFeralAV4, GaitRunStumbleAV4, GaitRunFeralBV4 },
            new[] { GaitWalkHeavyAV4, GaitWalkHeavyBV4, GaitWalkHeavyV3 },
            new[] { GaitWalkStompAV4, GaitWalkStompBV4 }
        };

        /// <summary>
        /// WP-096 AP-033 p2 · the tier's AP-022 gaits, used only when the set carries no member of <see cref="ZombieGaitPools"/> (a set
        /// built before AP-033); without those too, the AP-009 fit fallback (ZombieSkinnedVisual.GaitIndexFor).
        /// </summary>
        public static readonly int[][] ZombieGaitFallbacks =
        {
            new[] { GaitShambleFastAV3, GaitShambleFastBV3 },
            new[] { GaitRunV3 },
            new[] { GaitWalkHeavyV3 },
            new[] { GaitWalkStompV3 }
        };
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
            /// <summary>AP-033 p3 in-place transition motor contract; null for gait/player clips.</summary>
            public ZombieTransitionData transition;
            /// <summary>
            /// WP-096 P3: this clip's playback-rate band — floor (applied while the body cruises) and cap; 0 = the
            /// <see cref="CadenceBand"/> default (Min / Max). Authored on the set asset (ArtAnimatorBuilder keeps it on a rebuild).
            /// </summary>
            public float minRate;
            public float maxRate;
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
            /// <summary>WP-071 r2: the set's class has a sustained-fire loop (FireLoop_V3 while FireHeld).</summary>
            public bool hasFireLoop;
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

        /// <summary>WP-096 P3: the clip's playback-rate floor (its own <c>minRate</c>, else <see cref="CadenceBand.Min"/>).</summary>
        public float MinRateOf(string clipName)
        {
            Entry e = Find(clipName);
            return e != null && e.minRate > 0f ? e.minRate : CadenceBand.Min;
        }

        /// <summary>WP-096 P3: the clip's playback-rate cap (its own <c>maxRate</c>, else <see cref="CadenceBand.Max"/>).</summary>
        public float MaxRateOf(string clipName)
        {
            Entry e = Find(clipName);
            return e != null && e.maxRate > 0f ? e.maxRate : CadenceBand.Max;
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

        // ---------------------------------------------------------------- WP-082 R3 locomotion tables

        /// <summary>
        /// WP-082 R3 (LOCO-SOZLESME §4, §7, §8, §11): one AP-026 8-way loop — the ring's honest-speed inputs. Field names are the
        /// data JSON's (<c>K/wp-082/data/loco_loops.json#loops[]</c>) in camelCase; template metres, 30 fps.
        /// </summary>
        [Serializable]
        public sealed class LocoLoop
        {
            /// <summary><c>clip</c> / <c>unity_take</c> (AP026_tp_&lt;ring&gt;_&lt;dir&gt;).</summary>
            public string clip;
            /// <summary>The 1-D tree slot of the ring: 1 walk, 2 run, 3 sprint (slot 0 = the stand, no loop row).</summary>
            public int ringSlot;
            /// <summary><c>dir_index</c>: <see cref="LocomotionV4.Directions"/> order (0 fwd … 7 fwd_l, clockwise).</summary>
            public int dirIndex;
            /// <summary><c>stride_cycle_m_template</c>: one cycle's travel at the natural cadence (× k on the hero).</summary>
            public float strideCycleMTemplate;
            /// <summary><c>cycle_seconds_natural</c>: one cycle's seconds after the child time scale.</summary>
            public float cycleSecondsNatural;
        }

        /// <summary>
        /// WP-082 R3: one LocomotionV4 clip's tables (start or stop; <c>K/wp-082/data/loco_v4_starts.json#starts[]</c> /
        /// <c>loco_v4_stops.json#stops[]</c>), precomputed by the builder — the runtime never parses JSON.
        /// </summary>
        [Serializable]
        public sealed class LocoV4Clip
        {
            /// <summary><c>clip</c> (the AP-026 take name, <see cref="LocomotionV4.ClipName"/>).</summary>
            public string clip;
            /// <summary><c>v4_id</c> (<see cref="LocomotionV4.StartId"/> / <see cref="LocomotionV4.StopId"/>).</summary>
            public int v4Id;
            /// <summary><c>samples</c>: frames in the clip (= loopPhaseEquivNormPerFrame length).</summary>
            public int samples;
            /// <summary><c>seconds</c>: the clip's length at rate 1.</summary>
            public float seconds;
            /// <summary>Start: <c>lut_t_at_dist.dist_step_m_template</c>.</summary>
            public float distStepMTemplate;
            /// <summary>Start: <c>lut_t_at_dist.t_norm</c> (33): normalised time at i × step of template travel.</summary>
            public float[] lutTAtDist = new float[0];
            /// <summary>Start: <c>exit.exit_t_norm</c>.</summary>
            public float exitTNorm = 1f;
            /// <summary>Start: <c>exit.loop_offset_norm</c> (the loop's normalised offset on the exit).</summary>
            public float loopOffsetNorm;
            /// <summary>Stop: <c>lut_t_at_remaining.remaining_step_m_template</c>.</summary>
            public float remainingStepMTemplate;
            /// <summary>Stop: <c>lut_t_at_remaining.t_norm</c> (33): earliest normalised time with ≤ i × step of template travel left.</summary>
            public float[] lutTAtRemaining = new float[0];
            /// <summary>Stop: <c>settle_t_norm</c> (the motor has stopped).</summary>
            public float settleTNorm = 1f;
            /// <summary><c>loop_phase_equiv_norm_per_frame</c> (samples): the loop phase (0 = foot_l) each frame stands for.</summary>
            public float[] loopPhaseEquivNormPerFrame = new float[0];
        }

        /// <summary>WP-082 R3: the locomotion tables the builder writes from <c>K/wp-082/data</c> (empty = AP-022 4-way path).</summary>
        [Serializable]
        public sealed class LocomotionTables
        {
            /// <summary><c>loco_constants.json#hero_stride_scale.default</c>: k, template metres → hero metres (measured k replaces it).</summary>
            public float heroStrideScale = 1f;
            /// <summary>The 1-D tree's thresholds, m/s on the hero, slot order (0 stand, 1 walk, 2 run, 3 sprint) — the same numbers the builder writes into the tree.</summary>
            public float[] ringThresholdMps = new float[0];
            /// <summary><c>v4_gait_speeds_mps_template</c> in <see cref="LocomotionV4.Gaits"/> order (walk, jog, run, sprint); ≤ 0 = gait off (jog in R3).</summary>
            public float[] v4GaitSpeedsMpsTemplate = new float[0];
            public List<LocoLoop> loops = new List<LocoLoop>();
            public List<LocoV4Clip> v4 = new List<LocoV4Clip>();

            /// <summary>The 8-way rings are complete (3 rings × 8 directions + 4 thresholds).</summary>
            public bool HasRings => loops != null && loops.Count >= 24 && ringThresholdMps != null && ringThresholdMps.Length >= 4;
        }

        [Tooltip("WP-082 R3: AP-026 8-way locomotion + v4 distance tables (builder, from K/wp-082/data). Empty = the AP-022 4-way path.")]
        [SerializeField] private LocomotionTables _locomotion = new LocomotionTables();

        /// <summary>WP-082 R3: the locomotion tables (never null).</summary>
        public LocomotionTables Locomotion => _locomotion ?? (_locomotion = new LocomotionTables());

        /// <summary>Editor (builder): the locomotion tables.</summary>
        public void ConfigureLocomotion(LocomotionTables tables) => _locomotion = tables ?? new LocomotionTables();

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
