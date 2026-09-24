using System;
using UnityEngine;

namespace DD.Prototype.Core.Config
{
    public enum WeaponFeelId
    {
        AssaultRifle = 0,
        AutoSmg = 1,
        CombatShotgun = 2,
        KaguraBell = 3,
        OfudaLoom = 4,
        TideJewel = 5
    }

    public enum ImpactFeelId
    {
        Fire = 0,
        FleshHit = 1,
        ArmorHit = 2,
        ArmorBreak = 3,
        CriticalHit = 4,
        Death = 5,
        HeavyDeath = 6,
        Explosion = 7,
        OfudaCollapse = 8,
        PlayerDamage = 9,
        PlayerDeath = 10
    }

    public enum FeelShakeKind
    {
        Rattle = 0,
        Punch = 1,
        Rumble = 2,
        Bump = 3
    }

    [Serializable]
    public sealed class AimAssistFeelData
    {
        public int defaultLevel = 2;
        public float maxAngleDegrees = 6f;
        public float maxDistanceMeters = 15f;
        public float lightAngleShare = 0.5f;
    }

    [Serializable]
    public sealed class AimFeelData
    {
        public float stickPointDistanceMeters = 12f;
        public float pointerMinDistanceMeters = 0.35f;
    }

    [Serializable]
    public sealed class CameraFeelData
    {
        public float heightMeters = 15f;
        public float backDistanceMeters = 13f;
        public float fieldOfViewDegrees = 42f;
        public float yawDegrees;
        public float focusHeightOffsetMeters = 1f;
        public float followSmoothSeconds = 0.12f;
        public float maxLookAheadMeters = 2.5f;
        public float lookAheadSmoothSeconds = 0.18f;
        public float pointerLookAheadFactor = 0.35f;
        public float occlusionFadeSeconds = 0.15f;
        public float occlusionTargetAlpha = 0.22f;
    }

    [Serializable]
    public sealed class ShakeKernelFeelData
    {
        public float rattleFrequency = 34f;
        public float rumbleFrequency = 7f;
        public float rumbleZShare = 0.35f;
        public float rumbleDirectionShare = 0.45f;
        public float rumbleRollDegreesPerMeter = 5f;
        public float punchDecayPower = 2f;
        public float punchCosinePiFactor = 3f;
        public float punchRollDegreesPerMeter = 4f;
        public float bumpDecayPower = 1.5f;
        public float bumpCosinePiFactor = 1.5f;
        public float bumpZShare = 0.3f;
    }

    [Serializable]
    public sealed class ShakeFeelData
    {
        public float designGain = 1f;
        public float nominalAggregateCapMeters = 0.22f;
        public int maxConcurrent = 12;
        public float rollDegreesPerMeter = 6f;
        public ShakeKernelFeelData kernels = new ShakeKernelFeelData();
    }

    [Serializable]
    public sealed class FireShakeFeelData
    {
        public float metersAtWeightOne = 0.12f;
        public float baseSeconds = 0.06f;
        public float secondsPerWeight = 0.05f;
    }

    [Serializable]
    public sealed class RecoilFeelData
    {
        public float damping = 0.82f;
        public float pitchDegreesPerMeter = 90f;
        public float springCoefficient = 0.25f;
        public float animatorFullStrengthKickMeters = 0.2f;
    }

    [Serializable]
    public sealed class PlayerDamageFeelData
    {
        public float fullReactionDamageFraction = 0.25f;
        public float minimumStrength = 0.35f;
        public float flashDecaySeconds = 0.35f;
    }

    [Serializable]
    public sealed class WeaponFeelRow
    {
        public WeaponFeelId id;
        public float recoilKickMeters;
        public float recoilRecoverySeconds;
        public float recoilUpShare;
        public float shakeWeight;
        public FeelShakeKind shakeKind;
        public float rumbleFraction;
        public int rumbleDurationMs;
    }

    [Serializable]
    public sealed class ImpactFeelRow
    {
        public ImpactFeelId id;
        public int cameraPriority;
        public float shakeAmplitudeMeters;
        public float shakeSeconds;
        public FeelShakeKind shakeKind;
        public float hitStopSeconds;
        public float rumbleFraction;
        public int rumbleDurationMs;
    }

    [Serializable]
    public sealed class HitStopFeelData
    {
        public bool enabled = true;
        public float minIntervalSeconds = 0.25f;
        public float presentationScale = 0.06f;
        public float maxContinuousSeconds = 0.04f;
    }

    [Serializable]
    public sealed class VignetteFeelData
    {
        public bool enabled = true;
        public float flashAlpha = 0.35f;
        public float lowHealthAlpha = 0.35f;
        public float lowHealthFraction = 0.3f;
        public int flashInFrames = 4;
        public int flashOutFrames = 12;
    }

    [Serializable]
    public sealed class DeathFeelData
    {
        public bool enabled = true;
        public float slowScale = 0.5f;
        public float slowInSeconds = 0.15f;
        public float holdSeconds = 1.2f;
        public float outSeconds = 0.45f;
        public float cameraPushInFraction = 0.12f;
        public float resultsDelaySeconds = 2.4f;
    }

    [Serializable]
    public sealed class FeelColor
    {
        public float r = 1f;
        public float g = 1f;
        public float b = 1f;
        public float a = 1f;

        public FeelColor() { }

        public FeelColor(float red, float green, float blue, float alpha = 1f)
        {
            r = red; g = green; b = blue; a = alpha;
        }

        public Color UnityColor => new Color(r, g, b, a);
    }

    [Serializable]
    public sealed class DamageNumberFeelData
    {
        public bool enabled = true;
        public int maxConcurrent = 30;
        public float mergeWindowSeconds = 0.35f;
        public float lifetimeSeconds = 0.85f;
        public float anchorOffsetYMeters = 0.4f;
        public float mergeAnchorBlend = 0.35f;
        public float driftExtentXMeters = 0.315f;
        public float driftRiseMeters = 1.26f;
        public float driftExtentZMeters = 0.18f;
        public float mergePopScale = 1.25f;
        public float mergePopSeconds = 0.12f;
        public float endScale = 0.85f;
        public float fadeStartNormalized = 0.6f;
        public int fontSize = 64;
        public float characterSize = 0.045f;
        public FeelColor normalColor = new FeelColor(1f, 1f, 1f);
        public FeelColor armorDefaultColor = new FeelColor(0.78f, 0.8f, 0.86f);
        public string criticalToken = "StateWarning";
        public string armorAccessibleToken = "AccentWisteria";
    }

    [Serializable]
    public sealed class RumbleFeelData
    {
        public float highMotorRatio = 0.6f;
        public float classicFallbackFraction = 0.5f;
        public int classicFallbackDurationMs = 60;
        public float specialFallbackFraction = 0.7f;
        public int specialFallbackDurationMs = 80;
    }

    [Serializable]
    public sealed class BudgetCategoryFeelRow
    {
        public string id;
        public int capacity;
    }

    [Serializable]
    public sealed class BudgetResourceFeelData
    {
        public int flipbook = 48;
        public int decal = 64;
        public int fragment = 8;
        public int smoke = 8;
        public int casing = 12;
        public int straw = 4;
        public int spark = 8;
        public int leaf = 6;
        public int paper = 4;
        public int bloodDecal = 128;
        public int bloodDroplet = 12;
    }

    [Serializable]
    public sealed class BudgetFeelData
    {
        public BudgetCategoryFeelRow[] categories;
        public float perFrameFraction = 0.25f;
        public int minStartsPerFrame = 2;
        public int importantStartMultiplier = 2;
        public float mergeDistanceMeters = 0.55f;
        public float mergeWindowSeconds = 0.08f;
        public BudgetResourceFeelData resources = new BudgetResourceFeelData();
    }

    [Serializable]
    public sealed class TelemetryFeelData
    {
        public bool enabledByDefault = true;
        public float uiRefreshHz = 4f;
        public int roundCapacity = 64;
        public int ttkCapacity = 512;
        public int lifeCapacity = 256;
        public int eventTraceCapacity = 2048;
        public bool captureEventTrace;
    }

    [Serializable]
    public sealed class FeelConfigData
    {
        public bool enabled = true;
        public AimAssistFeelData aimAssist = new AimAssistFeelData();
        public AimFeelData aim = new AimFeelData();
        public CameraFeelData camera = new CameraFeelData();
        public ShakeFeelData shake = new ShakeFeelData();
        public FireShakeFeelData fireShake = new FireShakeFeelData();
        public RecoilFeelData recoil = new RecoilFeelData();
        public PlayerDamageFeelData playerDamage = new PlayerDamageFeelData();
        public WeaponFeelRow[] weapons;
        public ImpactFeelRow[] impacts;
        public HitStopFeelData hitStop = new HitStopFeelData();
        public VignetteFeelData vignette = new VignetteFeelData();
        public DeathFeelData death = new DeathFeelData();
        public DamageNumberFeelData damageNumbers = new DamageNumberFeelData();
        public RumbleFeelData rumble = new RumbleFeelData();
        public BudgetFeelData budgets = new BudgetFeelData();
        public TelemetryFeelData telemetry = new TelemetryFeelData();
    }

    /// <summary>The only writable designer source for combat presentation feel.</summary>
    [CreateAssetMenu(fileName = "DD_FeelConfig", menuName = "Deaths Demolition/Feel Config", order = 1)]
    public sealed class FeelConfig : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;
        public const string ResourcesPath = "DD_FeelConfig";

        [SerializeField] private int _schemaVersion = CurrentSchemaVersion;
        [SerializeField] private string _profileId = "m4-main-22c8ead";
        [SerializeField] private string _baseCommit = "22c8ead";
        [SerializeField] private FeelConfigData _data;

        public int SchemaVersion => _schemaVersion;
        public string ProfileId => _profileId;
        public string BaseCommit => _baseCommit;
        public FeelConfigData AuthoredData => _data;

        private void OnEnable()
        {
            if (_data == null || _data.weapons == null || _data.impacts == null)
            {
                _data = CreateDefaults();
            }
        }

        public bool Apply(out string error)
        {
            return FeelRuntime.QueueAuthored(_data, _profileId, _baseCommit, out error);
        }

        public static FeelConfigData CreateDefaults()
        {
            var data = new FeelConfigData();
            data.weapons = new[]
            {
                Weapon(WeaponFeelId.AssaultRifle, .10f, .10f, .15f, .30f, FeelShakeKind.Rattle, .67f, 70),
                Weapon(WeaponFeelId.AutoSmg, .05f, .055f, .10f, .18f, FeelShakeKind.Rattle, .5f, 45),
                Weapon(WeaponFeelId.CombatShotgun, .34f, .30f, .30f, 1f, FeelShakeKind.Punch, 1f, 120),
                Weapon(WeaponFeelId.KaguraBell, .16f, .125f, .15f, .85f, FeelShakeKind.Rattle, 1f, 120),
                Weapon(WeaponFeelId.OfudaLoom, .03f, .125f, .15f, .12f, FeelShakeKind.Rattle, .4f, 50),
                Weapon(WeaponFeelId.TideJewel, .08f, .125f, .15f, .30f, FeelShakeKind.Rattle, .7f, 90)
            };
            data.impacts = new[]
            {
                Impact(ImpactFeelId.Fire, 10, 0f, 0f, FeelShakeKind.Rattle, 0f, 0f, 0),
                Impact(ImpactFeelId.FleshHit, 20, 0f, 0f, FeelShakeKind.Bump, 0f, 0f, 0),
                Impact(ImpactFeelId.ArmorHit, 30, 0f, 0f, FeelShakeKind.Bump, 0f, 0f, 0),
                Impact(ImpactFeelId.ArmorBreak, 40, .075f, .12f, FeelShakeKind.Bump, .035f, 0f, 0),
                Impact(ImpactFeelId.CriticalHit, 50, 0f, 0f, FeelShakeKind.Bump, 0f, 0f, 0),
                Impact(ImpactFeelId.Death, 60, 0f, 0f, FeelShakeKind.Bump, 0f, 0f, 0),
                Impact(ImpactFeelId.HeavyDeath, 70, .095f, .18f, FeelShakeKind.Rumble, .04f, 0f, 0),
                Impact(ImpactFeelId.Explosion, 80, .12f, .2f, FeelShakeKind.Rumble, 0f, .8f, 100),
                Impact(ImpactFeelId.OfudaCollapse, 80, .085f, .2f, FeelShakeKind.Bump, 0f, .8f, 100),
                Impact(ImpactFeelId.PlayerDamage, 90, .09f, .15f, FeelShakeKind.Bump, 0f, 1f, 120),
                Impact(ImpactFeelId.PlayerDeath, 100, .15f, .2f, FeelShakeKind.Rumble, 0f, 0f, 0)
            };
            data.budgets.categories = new[]
            {
                Budget("Impact", 40), Budget("ArmorHit", 40), Budget("Blood", 24),
                Budget("ArmorBreak", 12), Budget("Death", 12), Budget("DamageNumber", 30),
                Budget("Tracer", 48), Budget("MuzzleFlash", 6), Budget("Link", 20),
                Budget("Explosion", 8), Budget("SpecialWeapon", 24)
            };
            return data;
        }

        private static WeaponFeelRow Weapon(WeaponFeelId id, float kick, float recovery, float up,
            float shake, FeelShakeKind kind, float rumble, int milliseconds)
        {
            return new WeaponFeelRow { id = id, recoilKickMeters = kick, recoilRecoverySeconds = recovery,
                recoilUpShare = up, shakeWeight = shake, shakeKind = kind,
                rumbleFraction = rumble, rumbleDurationMs = milliseconds };
        }

        private static ImpactFeelRow Impact(ImpactFeelId id, int priority, float amplitude, float seconds,
            FeelShakeKind kind, float stop, float rumble, int milliseconds)
        {
            return new ImpactFeelRow { id = id, cameraPriority = priority, shakeAmplitudeMeters = amplitude,
                shakeSeconds = seconds, shakeKind = kind, hitStopSeconds = stop,
                rumbleFraction = rumble, rumbleDurationMs = milliseconds };
        }

        private static BudgetCategoryFeelRow Budget(string id, int capacity)
        {
            return new BudgetCategoryFeelRow { id = id, capacity = capacity };
        }
    }

    public static class WeaponFeelIds
    {
        public static WeaponFeelId From(ClassicWeaponId id)
        {
            switch (id)
            {
                case ClassicWeaponId.AutoSmg: return WeaponFeelId.AutoSmg;
                case ClassicWeaponId.CombatShotgun: return WeaponFeelId.CombatShotgun;
                case ClassicWeaponId.AssaultRifle: return WeaponFeelId.AssaultRifle;
                // WP-069 B2 (interim until S-07's FeelConfig v2 rows per weaponId): the class sibling's row.
                default:
                    switch (DD.Prototype.Core.Weapons.Arsenal.LegacyPoseIndex(id))
                    {
                        case 1: return WeaponFeelId.AutoSmg;
                        case 2: return WeaponFeelId.CombatShotgun;
                        default: return WeaponFeelId.AssaultRifle;
                    }
            }
        }

        public static WeaponFeelId From(SpecialWeaponId id)
        {
            switch (id)
            {
                // WP-069 B4 (interim until S-07's v2 rows): Raiju → the old bell row, Namekuji → the loom row.
                case SpecialWeaponId.Namekuji: return WeaponFeelId.OfudaLoom;
                default: return WeaponFeelId.KaguraBell;
            }
        }
    }
}
