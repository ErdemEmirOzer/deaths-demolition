using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-033 (ADR-033-1) · Tuning of the threat signal. Every number is PROTOTYPE-ONLY (technical
    /// defaults; DEC-065 names the inputs, not the weights). <see cref="Active"/> = the Resources
    /// asset when present, else the code defaults (not shipped).
    /// </summary>
    public sealed class ThreatSettings : ScriptableObject
    {
        public const string ResourcesPath = "DD_Threat";
        public const int SectorCount = 8;   // 360° / 8 (encirclement rule, package decision)

        [Header("Proximity / encirclement (DEC-032)")]
        [PrototypeOnly(PendingDecisions.None, "Radius (m) inside which an enemy counts as near.", true)] public float NearRadius = 6f;
        [PrototypeOnly(PendingDecisions.None, "Near enemies that saturate the proximity component.", true)] public int ProximityFullCount = 8;
        [PrototypeOnly(PendingDecisions.None, "Occupied sectors (of 8) at or above which the player is encircled.", true)] [Range(1, SectorCount)] public int EncircleMinSectors = 5;

        [Header("Tier presence (DEC-065 / DEC-067)")]
        [PrototypeOnly(PendingDecisions.None, "Presence weight of the strongest live version (Standard 0).", true)]
        public float SpecialPresence = 0.25f, ElitePresence = 0.5f, UltraPresence = 1f;

        [Header("Attack preparation / rear attack (REQ-105)")]
        [PrototypeOnly(PendingDecisions.None, "Radius (m) inside which a winding-up attack counts.", true)] public float AttackPrepRadius = 4.5f;
        [PrototypeOnly(PendingDecisions.None, "Winding-up attackers that saturate the component.", true)] public int AttackPrepFullCount = 3;
        [PrototypeOnly(PendingDecisions.None, "Angle from the view forward (deg) beyond which a winding-up attacker is 'behind'.", true)] [Range(60f, 180f)] public float RearAngleDegrees = 100f;

        [Header("Player danger")]
        [PrototypeOnly(PendingDecisions.None, "Health ratio at or below which the low-health flag is raised.", true)] [Range(0f, 1f)] public float LowHealthThreshold = 0.35f;

        [Header("Composition weights (sum ≤ 1 → raw intensity)")]
        [PrototypeOnly(PendingDecisions.None, "Component weights: proximity, encirclement, tier, round progress, player danger, attack preparation.", true)]
        public float ProximityWeight = 0.30f, EncirclementWeight = 0.20f, TierWeight = 0.15f, RoundWeight = 0.10f, DangerWeight = 0.15f, AttackPrepWeight = 0.10f;

        [Header("Smoothing / hysteresis (DEC-065: layers never jump)")]
        [PrototypeOnly(PendingDecisions.None, "Intensity rise rate per second (attack).", true)] public float RiseRate = 1.5f;
        [PrototypeOnly(PendingDecisions.None, "Intensity fall rate per second (release).", true)] public float FallRate = 0.45f;
        [PrototypeOnly(PendingDecisions.None, "Intensity ceiling between rounds / outside the fight (DEC-065 pull-back).", true)] [Range(0f, 0.5f)] public float BetweenRoundsCeiling = 0.15f;
        [PrototypeOnly(PendingDecisions.None, "Music layer thresholds (layer 1..3) on the smoothed intensity.", true)]
        public float Layer1Threshold = 0.2f, Layer2Threshold = 0.45f, Layer3Threshold = 0.7f;
        [PrototypeOnly(PendingDecisions.None, "Hysteresis band around each layer threshold.", true)] [Range(0f, 0.2f)] public float LayerHysteresis = 0.05f;
        [PrototypeOnly(PendingDecisions.None, "Seconds a flag (encircled / rear attack) stays raised after its cause ends (readable warning).", true)] public float FlagHoldSeconds = 0.6f;

        [Header("WP-076 §6 · rear awareness by camera mode (TopDown, ThirdPerson, FirstPerson)")]
        [PrototypeOnly(PendingDecisions.None, "WP-076 §6: fallback rear angle (deg) when no camera rig answers IsInView; IsInView margin (deg).", true)]
        public float[] RearAngleByMode = { 100f, 110f, 75f };
        public float[] RearViewMarginByMode = { 0f, 10f, 0f };
        [PrototypeOnly(PendingDecisions.None, "WP-076 §6 RearLevel: Aware distance (m; 0 = none), Warn intensity, Warn distance (FP; 0 = none), Warn pair distance (TP: ≥ 2 bodies within; 0 = none), Alert distance (FP bite band; 0 = none), level hold (s).", true)]
        public float[] RearAwareMetres = { 0f, 6f, 7f };
        public float[] RearWarnIntensity = { 0.40f, 0.25f, 0.15f };
        public float[] RearWarnMetres = { 0f, 0f, 4f };
        public float[] RearWarnPairMetres = { 0f, 5f, 0f };
        public float[] RearAlertMetres = { 0f, 0f, 2.2f };
        public float[] RearHoldSeconds = { 0.6f, 0.6f, 0.45f };
        [PrototypeOnly(PendingDecisions.None, "WP-076 §6 rear_threat beat: raise distance (m) Standard / Special+Elite+Ultra, re-arm distance (m); attack lead after the beat (s) by mode.", true)]
        public float RearBeatMetres = 6f, RearBeatMetresHeavy = 8f, RearBeatRearmMetres = 9f;
        public float[] RearWarnLeadSeconds = { 0f, 0.5f, 0.8f };
        [PrototypeOnly(PendingDecisions.None, "WP-076 §5.1 rear-hemisphere spawn share cap by mode (1 = none), sliding window (spawns), factor below the cap, factor at the cap when no other path-ready point exists.", true)]
        public float[] RearSpawnCap = { 1f, 0.45f, 0.35f };
        public int RearSpawnWindow = 8;
        public float RearSpawnBelowCapFactor = 0.7f, RearSpawnNoAlternativeFactor = 0.25f;

        public static int ModeIndex(int mode) => Mathf.Clamp(mode, 0, 2);
        public float ByMode(float[] table, int mode, float fallback) => table != null && table.Length > ModeIndex(mode) ? table[ModeIndex(mode)] : fallback;

        private static ThreatSettings s_active;

        public static ThreatSettings Active
        {
            get
            {
                if (s_active == null)
                {
                    s_active = Resources.Load<ThreatSettings>(ResourcesPath);
                    if (s_active == null)
                    {
                        s_active = CreateInstance<ThreatSettings>();
                        s_active.name = "DD_Threat (code defaults)";
                        s_active.hideFlags = HideFlags.HideAndDontSave;
                    }
                }

                return s_active;
            }
        }

        public static void SetActive(ThreatSettings settings) => s_active = settings;
        public static void ResetStatics() => s_active = null;
    }
}
