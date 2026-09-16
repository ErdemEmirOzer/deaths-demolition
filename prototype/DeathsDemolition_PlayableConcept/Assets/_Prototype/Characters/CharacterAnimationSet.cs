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
        public const string AttackStyle = "AttackStyle"; // int: 0 swipe, 1 lunge, 2 sweep
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
        public const string Fire = "Fire";               // trigger, additive layer (player)
        public const string Pickup = "Pickup";           // trigger (player)
        public const string MoveX = "MoveX";             // player: local strafe velocity / reference speed
        public const string MoveY = "MoveY";             // player: local forward velocity / reference speed
        public const string LocoRate = "LocoRate";       // player: |v| / reference speed (state speed multiplier)
        // WP-032 telegraph exaggeration bridge (additive upper-body emphasis over the wind-up; DEC-029 events untouched)
        public const string Telegraph = "Telegraph";         // trigger
        public const string TelegraphStyle = "TelegraphStyle"; // int: 0 swipe, 1 lunge, 2 sweep
        public const string TelegraphRate = "TelegraphRate";   // float: 1 / wind-up seconds (the 1 s clip spans the wind-up)

        // ----------------------------------------------------------------- states
        public const string LocomotionState = "Locomotion";
        public const string IdleState = "Idle";
        public const string GaitState = "Gait";
        public const string AttackSwipeState = "Attack_Swipe";
        public const string AttackLungeState = "Attack_Lunge";
        public const string AttackSweepState = "Attack_Sweep";
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

        // -------------------------------------------------------- AP-009 clip names
        public const string PlayerIdle = "AP009_player_idle";
        public const string PlayerWalkFwd = "AP009_player_walk_fwd";
        public const string PlayerWalkBack = "AP009_player_walk_back";
        public const string PlayerStrafeLeft = "AP009_player_strafe_left";
        public const string PlayerStrafeRight = "AP009_player_strafe_right";
        public const string PlayerFireRecoil = "AP009_player_fire_recoil";
        public const string PlayerHitLight = "AP009_player_hit_light";
        public const string PlayerDeathFwd = "AP009_player_death_fwd";
        public const string PlayerDeathBack = "AP009_player_death_back";
        public const string PlayerPickup = "AP009_player_pickup_special";
        public const string ZombieIdleSway = "AP009_zombie_idle_sway";
        public const string ZombieIdleTwitch = "AP009_zombie_idle_twitch";
        public const string ZombieShambleA = "AP009_zombie_shamble_A";
        public const string ZombieShambleB = "AP009_zombie_shamble_B";
        public const string ZombieShambleC = "AP009_zombie_shamble_C";
        public const string ZombieRun = "AP009_zombie_run";
        public const string ZombieAttackLunge = "AP009_zombie_attack_lunge";
        public const string ZombieAttackSweep = "AP009_zombie_attack_sweep";
        public const string ZombieAttackSwipe = "AP009_zombie_attack_swipe";
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

        public static readonly string[] PlayerClips =
        {
            PlayerIdle, PlayerWalkFwd, PlayerWalkBack, PlayerStrafeLeft, PlayerStrafeRight,
            PlayerFireRecoil, PlayerHitLight, PlayerDeathFwd, PlayerDeathBack, PlayerPickup
        };

        public static readonly string[] ZombieClips =
        {
            ZombieIdleSway, ZombieIdleTwitch, ZombieShambleA, ZombieShambleB, ZombieShambleC, ZombieRun,
            ZombieAttackLunge, ZombieAttackSweep, ZombieAttackSwipe,
            ZombieHitLightL, ZombieHitLightR, ZombieHitHeavyStagger,
            ZombieHitRegionHead, ZombieHitRegionTorso, ZombieHitRegionLeg,
            ZombieDeathForward, ZombieDeathBackward, ZombieDeathSide, ZombieDeathKneel, ZombieRise
        };

        public const int ClipCount = 30;
        public const int Fps = 30;

        /// <summary>The zombie death clips in DeathStyle order (0 fwd, 1 back, 2 side, 3 kneel).</summary>
        public static readonly string[] ZombieDeathClips = { ZombieDeathForward, ZombieDeathBackward, ZombieDeathSide, ZombieDeathKneel };
        /// <summary>The zombie attack clips in AttackStyle order (0 swipe, 1 lunge, 2 sweep).</summary>
        public static readonly string[] ZombieAttackClips = { ZombieAttackSwipe, ZombieAttackLunge, ZombieAttackSweep };
        /// <summary>The zombie gait clips in Gait order (0 A, 1 B, 2 C, 3 run).</summary>
        public static readonly string[] ZombieGaitClips = { ZombieShambleA, ZombieShambleB, ZombieShambleC, ZombieRun };
    }

    /// <summary>
    /// WP-016/018 · The clips a character's Animator plays, as one asset: the WP-016 rig-test
    /// pair (idle / walk, 1-D blend on <c>Speed</c>) stays as the fallback when AP-009 is
    /// absent, and the WP-018 table carries the 30 AP-009 clips with their manifest data
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
