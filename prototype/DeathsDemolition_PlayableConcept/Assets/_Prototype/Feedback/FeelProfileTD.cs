using System;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Feedback
{
    /// <summary>WP-095 his sözleşmesi §4.0 rows (23; Heartbeat = the low-health beat of §4.7).</summary>
    public enum FeelEventId
    {
        Shot = 0, Hit = 1, Crit = 2, Kill = 3, MultiKill_T1 = 4, MultiKill_T2 = 5, MultiKill_T3 = 6, ArmorBreak = 7,
        HeavyDeath_Elite = 8, HeavyDeath_Ultra = 9, Explosion = 10, PlayerDamaged_Light = 11, PlayerDamaged_Heavy = 12,
        LowHealthEnter = 13, LowHealthExit = 14, ReloadStart = 15, ReloadEnd = 16, DryFire = 17, GoldPickup = 18,
        RoundStart = 19, RoundEnd = 20, LastZombie = 21, Heartbeat = 22,
    }

    /// <summary>One weapon's top-down feel (his sözleşmesi §3).</summary>
    [Serializable]
    public struct WeaponFeelTD
    {
        public string label;                                    // ar_1 … sw_gel, pistol (reserved)
        public bool special;
        public ClassicWeaponId classic;
        public SpecialWeaponId specialId;
        public FeelWeight kickSpring;                           // W1..W4 (CameraModesFeel kick springs)
        [Range(0f, 1f)] public float fireTraumaAdd;             // K1
        [Range(0f, 1f)] public float fireTraumaCap;
        [Range(0f, .14f)] public float kickMeters;              // K3, −aim
        [Range(0f, 1.5f)] public float fireLeanMeters;          // K4
        [Range(0f, 2f)] public float hitStopScale;
        [Range(0f, 2f)] public float muzzleLightScale;          // × VfxWeaponProfiles
        [Range(0f, 1f)] public float rumbleLow;
        [Range(0f, 1f)] public float rumbleHigh;
        [Range(0f, .4f)] public float rumbleSeconds;            // sustained: the fade after the trigger lets go
        public bool rumbleSustained;
        [Range(0f, .15f)] public float recoilBackMeters;        // WP-087 arm-channel cap
        [Range(0f, 12f)] public float recoilLeanDegrees;        // S-06 upper-body lean
        public bool singleShotClass;                            // §4.0 / §4.3 "tek-atış sınıfı": a critical kill stops the world
    }

    /// <summary>One event's top-down camera / stop / rumble (his sözleşmesi §4.0).</summary>
    [Serializable]
    public struct EventFeelTD
    {
        public FeelEventId id;
        [Range(0, 100)] public int priority;
        [Range(0, 4)] public int localFreezeFrames;
        [Range(0, 4)] public int globalStopFrames;
        [Range(0f, 1f)] public float worldTraumaAdd;            // K2
        [Range(0f, .14f)] public float kickMeters;              // K3
        [Range(-.1f, .1f)] public float zoomPunch;              // K5; −.08 = 8 % closer
        [Range(0f, 1f)] public float rumbleLow;
        [Range(0f, 1f)] public float rumbleHigh;
        [Range(0f, .4f)] public float rumbleSeconds;
    }

    /// <summary>
    /// WP-095 (R3) · the top-down feel profile: every TD feel layer reads its switch and its values here
    /// (K/wp-095/HIS-SOZLESMESI.md §8). The player's settings (shake scale, rumble scale, reduced motion) are not here — S-04.
    /// Resources asset <see cref="ResourceName"/>; a runtime default from <see cref="ResetToDefaults"/> when absent.
    /// </summary>
    [CreateAssetMenu(fileName = "FeelProfile_TD", menuName = "Deaths Demolition/Feel Profile TD")]
    public sealed class FeelProfileTD : ScriptableObject
    {
        public const string ResourceName = "FeelProfile_TD";

        [Header("Katman anahtarları")]
        public bool fireTrauma = true;
        public bool worldTrauma = true;
        public bool kick = true;
        public bool fireLean = true;
        public bool zoomPunch = true;
        public bool hitStopLocal = true;
        public bool hitStopGlobal = true;
        public bool lastZombieSlowMo = true;
        public bool muzzleLight = true;
        public bool enemyFlash = true;
        public bool hitNudge = true;
        public bool killPop = true;
        public bool rumble = true;
        public bool lowHealthVignette = true;
        public bool lowHealthDesaturate = true;
        public bool lowHealthHeartbeatRumble = true;

        [Header("Kamera tavanları")]
        [Range(0f, .22f)] public float aggregateCapMeters = .22f;
        [Range(0f, 1f)] public float rollCapDegrees = .4f;
        [Range(0f, 1f)] public float aimLateralShare = .35f;
        [Range(0f, .08f)] public float sustainedKickCapMeters = .04f;
        [Range(2f, 4f)] public float lookAheadTotalCapMeters = 3f;

        [Header("K1 / K2 travma")]
        [Range(.1f, .3f)] public float fireMaxOffsetMeters = .24f;
        [Range(.1f, .3f)] public float worldMaxOffsetMeters = .24f;
        [Range(1f, 3f)] public float traumaExponent = 2f;
        [Range(.5f, 4f)] public float fireDecayPerSecond = 1.6f;
        [Range(.5f, 4f)] public float worldDecayPerSecond = 1.1f;
        [Range(6f, 28f)] public float fireNoiseHz = 22f;        // < 30 Hz (60 fps Nyquist)
        [Range(3f, 20f)] public float worldNoiseHz = 9f;

        [Header("K4 / K5")]
        [Range(0f, .5f)] public float leanInSeconds = .25f;
        [Range(0f, .5f)] public float leanHoldSeconds = .15f;
        [Range(0f, 1f)] public float leanOutSeconds = .5f;
        [Range(0f, .2f)] public float zoomInSeconds = .08f;
        [Range(0f, .6f)] public float zoomOutSeconds = .35f;
        [Range(0f, .5f)] public float roundStartBreathInSeconds = .25f;      // §4.10 "nefes": eased in, never a pop
        [Range(.2f, 2f)] public float roundStartBreathSeconds = .8f;         // back to 0, easeInOutSine
        [Range(0f, 20f)] public float roundStartClearRadiusMeters = 8f;      // no breath with a zombie this close

        [Header("Hit-stop")]
        [Range(.03f, .2f)] public float globalStopTimeScale = .06f;
        [Range(.25f, 1f)] public float globalStopMinInterval = .40f;
        [Range(1, 4)] public int maxStopFrames = 4;
        [Range(1, 16)] public int maxLocalFreezes = 8;
        [Range(0f, 1f)] public float autoGuardHeldSeconds = .35f;
        [Range(300f, 900f)] public float autoGuardMinRpm = 500f;
        [Range(0, 4)] public int critKillGlobalFrames = 2;                   // §4.0 Kill: "kritik + tek-atış sınıfı 2"

        [Header("Son zombi")]
        [Range(.1f, 1f)] public float lastZombieTimeScale = .25f;
        [Range(.1f, 1f)] public float lastZombieHoldSeconds = .45f;
        [Range(0f, .6f)] public float lastZombieInSeconds = .08f;
        [Range(0f, .6f)] public float lastZombieOutSeconds = .30f;
        [Range(0f, .6f)] public float lastZombieFocusBlend = .30f;
        [Range(.5f, 1.5f)] public float roundEndHoldSafetySeconds = 1f;

        [Header("Zombi tepkisi · birleştirme")]
        [Range(0f, 2f)] public float flashWhiteIntensity = .85f;
        [Range(0f, 2f)] public float flashCritIntensity = 1f;
        [Range(1, 6)] public int flashHoldFrames = 1;
        [Range(1, 6)] public int flashFadeFrames = 3;
        [Range(1, 6)] public int critFadeFrames = 4;
        [Range(.05f, .3f)] public float flashMinIntervalSeconds = .10f;
        [Range(.05f, .3f)] public float killBeatMinInterval = .12f;
        [Range(1f, 1.2f)] public float killBodyPopScale = 1.06f;
        [Range(0f, .2f)] public float killBodyPopSeconds = .05f;
        [Range(0f, .2f)] public float nudgeSeconds = .08f;
        [Range(0f, .2f)] public float killMergeSeconds = .08f;
        [Range(.2f, 1f)] public float multiKillChainWindow = .6f;
        [Range(2, 12)] public int multiKillTier1 = 3;
        [Range(2, 12)] public int multiKillTier2 = 5;
        [Range(2, 12)] public int multiKillTier3 = 8;

        [Header("Düşük can")]
        [Range(.1f, .5f)] public float lowHealthEnter = .30f;
        [Range(.1f, .5f)] public float lowHealthExit = .38f;
        [Range(0f, .5f)] public float vignettePulseMin = .22f;
        [Range(0f, .5f)] public float vignettePulseMax = .38f;
        [Range(40f, 140f)] public float heartbeatBpmAtEnter = 70f;
        [Range(40f, 140f)] public float heartbeatBpmAtFloor = 110f;
        [Range(-60f, 0f)] public float desaturate = -25f;
        [Range(-60f, 0f)] public float desaturateFloor = -35f;
        [Range(0f, 1f)] public float desaturateInSeconds = .4f;
        [Range(0f, 1f)] public float exitFadeSeconds = .6f;

        [Header("Namlu ışığı · titreşim")]
        [Range(.5f, 1f)] public float sustainedFlickerFloor = .75f;
        [Range(2f, 12f)] public float autoLightRangeCapMeters = 6f;
        [Range(0f, 1f)] public float rumbleCeiling = 1f;
        [Range(.1f, .4f)] public float rumbleMaxSeconds = .4f;
        [Range(0f, .3f)] public float rumbleEventOverSustained = .15f;

        public WeaponFeelTD[] weapons;                          // 13 rows (§3)
        public EventFeelTD[] events;                            // 23 rows (§4.0), indexed by FeelEventId

        // ------------------------------------------------------------------ lookup

        public WeaponFeelTD Weapon(ClassicWeaponId id)
        {
            if (weapons != null)
                for (int i = 0; i < weapons.Length; i++)
                    if (!weapons[i].special && weapons[i].classic == id && weapons[i].label != PistolLabel) return weapons[i];
            return default;
        }

        public WeaponFeelTD Weapon(SpecialWeaponId id)
        {
            if (weapons != null)
                for (int i = 0; i < weapons.Length; i++)
                    if (weapons[i].special && weapons[i].specialId == id) return weapons[i];
            return default;
        }

        public EventFeelTD Event(FeelEventId id)
        {
            int i = (int)id;
            if (events != null && i < events.Length && events[i].id == id) return events[i];
            if (events != null) for (int k = 0; k < events.Length; k++) if (events[k].id == id) return events[k];
            return default;
        }

        /// <summary>The FeelEvents thresholds this profile carries (multi-kill, low health; the rest keep their defaults).</summary>
        public FeelEventThresholds Thresholds()
        {
            FeelEventThresholds t = FeelEventThresholds.Default;
            t.MultiKillChainWindowSeconds = multiKillChainWindow;
            t.MultiKillGroupMin = multiKillTier1;
            t.MultiKillTier2 = multiKillTier2;
            t.MultiKillTier3 = multiKillTier3;
            t.LowHealthEnter = lowHealthEnter;
            t.LowHealthExit = lowHealthExit;
            return t;
        }

        // ------------------------------------------------------------------ active profile

        private static FeelProfileTD s_active;
        private static bool s_tried;

        /// <summary>The Resources asset, or a default instance when it is absent.</summary>
        public static FeelProfileTD Active
        {
            get
            {
                if (s_active == null && !s_tried)
                {
                    s_tried = true;
                    s_active = Resources.Load<FeelProfileTD>(ResourceName);
                    if (s_active != null) FeelEvents.Thresholds = s_active.Thresholds();
                }

                if (s_active == null)
                {
                    s_active = CreateInstance<FeelProfileTD>();
                    s_active.ResetToDefaults();
                    s_active.name = ResourceName + " (runtime default)";
                    s_active.hideFlags = HideFlags.HideAndDontSave;
                    FeelEvents.Thresholds = s_active.Thresholds();
                }

                return s_active;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_active = null;
            s_tried = false;
        }

#if UNITY_EDITOR
        /// <summary>Tests: use <paramref name="profile"/> (null = load again).</summary>
        public static void SetActiveForTests(FeelProfileTD profile)
        {
            s_active = profile;
            s_tried = profile != null;
            FeelEvents.Thresholds = profile != null ? profile.Thresholds() : FeelEventThresholds.Default;
        }
#endif

        // ------------------------------------------------------------------ defaults (his sözleşmesi §3 / §4.0)

        public const string PistolLabel = "pistol";

        private void Reset() => ResetToDefaults();

        public void ResetToDefaults()
        {
            weapons = DefaultWeapons();
            events = DefaultEvents();
        }

        private static WeaponFeelTD W(string label, ClassicWeaponId id, FeelWeight spring, float add, float cap, float kick, float lean,
            float hs, float low, float high, float seconds, bool sustained, float back, float leanDeg) => new WeaponFeelTD
        {
            label = label, classic = id, kickSpring = spring, fireTraumaAdd = add, fireTraumaCap = cap, kickMeters = kick,
            fireLeanMeters = lean, hitStopScale = hs, muzzleLightScale = 1f, rumbleLow = low, rumbleHigh = high,
            rumbleSeconds = seconds, rumbleSustained = sustained, recoilBackMeters = back, recoilLeanDegrees = leanDeg,
        };

        private static WeaponFeelTD S(string label, SpecialWeaponId id, FeelWeight spring, float add, float cap, float kick, float lean,
            float hs, float low, float high, float seconds, float back, float leanDeg)
        {
            WeaponFeelTD row = W(label, default, spring, add, cap, kick, lean, hs, low, high, seconds, false, back, leanDeg);
            row.special = true;
            row.specialId = id;
            return row;
        }

        public static WeaponFeelTD[] DefaultWeapons()
        {
            WeaponFeelTD[] rows = DefaultWeaponRows();
            for (int i = 0; i < rows.Length; i++)
                rows[i].singleShotClass = rows[i].label == "ar_2" || rows[i].label == "sg_1" || rows[i].label == "sg_2" || rows[i].label == "sr_1";
            return rows;
        }

        private static WeaponFeelTD[] DefaultWeaponRows() => new[]
        {
            W("smg_1", ClassicWeaponId.AutoSmg, FeelWeight.W1, .20f, .41f, .015f, .6f, .5f, .10f, .30f, .06f, true, .05f, 2f),
            W("smg_2", ClassicWeaponId.Smg2, FeelWeight.W1, .33f, .43f, .020f, .6f, .6f, .12f, .35f, .05f, false, .06f, 2.5f),
            W("ar_1", ClassicWeaponId.AssaultRifle, FeelWeight.W2, .31f, .46f, .025f, .6f, 1f, .15f, .35f, .06f, true, .10f, 3f),
            W("ar_2", ClassicWeaponId.Ar2, FeelWeight.W2, .42f, .50f, .035f, .6f, 1f, .20f, .45f, .06f, false, .10f, 3.5f),
            W("lmg_1", ClassicWeaponId.Lmg1, FeelWeight.W3, .24f, .52f, .025f, .7f, .75f, .30f, .30f, .06f, true, .09f, 3f),
            W("lmg_2", ClassicWeaponId.Lmg2, FeelWeight.W3, .41f, .55f, .035f, .7f, .9f, .35f, .30f, .07f, false, .11f, 4f),
            W("sg_1", ClassicWeaponId.CombatShotgun, FeelWeight.W2, .68f, .75f, .10f, .4f, 1.25f, .60f, .70f, .10f, false, .07f, 7f),
            W("sg_2", ClassicWeaponId.Sg2, FeelWeight.W2, .72f, .78f, .11f, .4f, 1.25f, .65f, .75f, .11f, false, .08f, 8f),
            W("sr_1", ClassicWeaponId.Sr1, FeelWeight.W3, .74f, .80f, .12f, 1.2f, 1.5f, .50f, .90f, .09f, false, .14f, 6f),
            W("rl_1", ClassicWeaponId.Rl1, FeelWeight.W4, .79f, .85f, .14f, .9f, 1f, .70f, .50f, .14f, false, .15f, 10f),
            S("sw_arc", SpecialWeaponId.Raiju, FeelWeight.W2, .55f, .70f, .06f, .6f, 1f, .40f, .80f, .12f, .08f, 5f),
            S("sw_gel", SpecialWeaponId.Namekuji, FeelWeight.W3, .20f, .35f, .02f, .6f, .5f, .25f, .10f, .08f, .04f, 2f),
            W(PistolLabel, ClassicWeaponId.AssaultRifle, FeelWeight.W1, .38f, .45f, .03f, .5f, .9f, .10f, .40f, .05f, false, .06f, 3f),
        };

        private static EventFeelTD E(FeelEventId id, int p, float k2, float k3, float k5, int local, int global, float low, float high,
            float seconds) => new EventFeelTD
        {
            id = id, priority = p, worldTraumaAdd = k2, kickMeters = k3, zoomPunch = k5, localFreezeFrames = local,
            globalStopFrames = global, rumbleLow = low, rumbleHigh = high, rumbleSeconds = seconds,
        };

        public static EventFeelTD[] DefaultEvents() => new[]
        {
            E(FeelEventId.Shot, 10, 0f, 0f, 0f, 0, 0, 0f, 0f, 0f),                         // K1 / K3 / rumble: the weapon row
            E(FeelEventId.Hit, 20, 0f, 0f, 0f, 0, 0, 0f, 0f, 0f),
            E(FeelEventId.Crit, 50, 0f, .01f, 0f, 2, 0, 0f, .20f, .03f),
            E(FeelEventId.Kill, 60, 0f, .02f, 0f, 2, 0, .10f, .25f, .04f),                 // crit kill: local 3; crit + single-shot class: global 2
            E(FeelEventId.MultiKill_T1, 65, .15f, .03f, 0f, 0, 0, .30f, .30f, .10f),
            E(FeelEventId.MultiKill_T2, 65, .25f, .04f, -.015f, 0, 2, .45f, .35f, .14f),    // global only in one trigger group
            E(FeelEventId.MultiKill_T3, 65, .35f, .05f, -.015f, 0, 3, .60f, .40f, .18f),
            E(FeelEventId.ArmorBreak, 40, .25f, .04f, 0f, 2, 2, .35f, .50f, .08f),
            E(FeelEventId.HeavyDeath_Elite, 70, .35f, .05f, 0f, 4, 3, .55f, .40f, .14f),
            E(FeelEventId.HeavyDeath_Ultra, 70, .50f, .07f, -.02f, 4, 4, .75f, .45f, .20f),
            E(FeelEventId.Explosion, 80, .85f, .10f, -.03f, 0, 0, .85f, .45f, .30f),          // × A(d)
            E(FeelEventId.PlayerDamaged_Light, 90, .20f, .05f, 0f, 0, 0, .45f, .55f, .12f),  // K2 linear in the damage fraction up to Heavy
            E(FeelEventId.PlayerDamaged_Heavy, 90, .60f, .09f, 0f, 0, 2, .75f, .80f, .20f),
            E(FeelEventId.LowHealthEnter, 0, 0f, 0f, 0f, 0, 0, 0f, 0f, 0f),
            E(FeelEventId.LowHealthExit, 0, 0f, 0f, 0f, 0, 0, 0f, 0f, 0f),
            E(FeelEventId.ReloadStart, 8, 0f, 0f, 0f, 0, 0, 0f, .12f, .04f),
            E(FeelEventId.ReloadEnd, 8, 0f, 0f, 0f, 0, 0, .05f, .20f, .05f),
            E(FeelEventId.DryFire, 8, 0f, 0f, 0f, 0, 0, 0f, .10f, .025f),
            E(FeelEventId.GoldPickup, 9, 0f, 0f, 0f, 0, 0, 0f, .08f, .02f),
            E(FeelEventId.RoundStart, 5, 0f, 0f, .04f, 0, 0, .30f, 0f, .15f),
            E(FeelEventId.RoundEnd, 5, 0f, 0f, 0f, 0, 0, 0f, 0f, 0f),
            E(FeelEventId.LastZombie, 95, .30f, .05f, -.08f, 4, 4, .60f, .30f, .35f),
            E(FeelEventId.Heartbeat, 0, 0f, 0f, 0f, 0, 0, .18f, 0f, .05f),                 // + .12 low motor 0.18 s later
        };
    }
}
