using DD.Prototype.Core;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Navigation
{
    /// <summary>
    /// WP-054 · Crowd / AI feel polish v1 — the knobs (PROTOTYPE-ONLY; game-designer single turn,
    /// 2026-09-17, values are proposals to be revised together at the end of the project):
    ///
    ///   flank approach     a share of each tier approaches at a fixed per-life angle (±35–70°) and straightens
    ///                      inside 3 m — the swarm reads as wrapping, not as a single file; heavy tiers walk in
    ///   wall friction      a body sliding along the world keeps 80 % speed (enter 0.10 s / exit 0.35 s hysteresis)
    ///   gate threshold     inside a Gate volume heavy bodies weigh ×1.5, light ×0.6 — the heavy one takes the
    ///                      threshold, the light ones open to the sides (never on the bridge: queue rule only)
    ///   queue variety      the stall threshold is rolled per life (0.45–0.85 s) and a queued light body sways
    ///                      laterally (0.08 m/s, 1.4–2.2 s period, ≤ 0.12 m) so a queue reads alive
    ///   corpse linger      per tier (0.55 / 0.8 / 1.5 / 3.0 s ± 15 %), ×0.4 near the body ceiling (DEC-023)
    ///   threat visibility  the on-screen spawn weight factor drops with the Threat Director layer (0.35 → 0.20 → 0.10)
    ///
    /// Code defaults ship; a Resources asset (DD_CrowdFeel) overrides for tuning, same pattern as ThreatSettings.
    /// </summary>
    public sealed class CrowdFeelSettings : ScriptableObject
    {
        public const string ResourcesPath = "DD_CrowdFeel";

        [Header("Flank approach (per life)")]
        [PrototypeOnly(PendingDecisions.None, "Share of lives per tier (Standard, Special, Elite, Ultra) that approach at an angle.", true)]
        public float[] FlankShare = { 0.35f, 0.60f, 0.10f, 0f };
        [PrototypeOnly(PendingDecisions.None, "Approach angle band, degrees (uniform per life, sign random).", true)]
        public float FlankAngleMin = 35f, FlankAngleMax = 70f;
        [PrototypeOnly(PendingDecisions.None, "Inside this distance to the target the approach straightens (blend over FlankBlendMetres).", true)]
        public float FlankStraightRadius = 3f, FlankBlendMetres = 2.5f;
        [PrototypeOnly(PendingDecisions.None, "Lateral offset cap of the flank goal, metres (kept on the NavMesh by the path query's projection).", true)]
        public float FlankOffsetMax = 2.5f;

        [Header("Wall friction")]
        [PrototypeOnly(PendingDecisions.None, "Speed factor while sliding along world geometry.", true)]
        [Range(0.5f, 1f)] public float WallFriction = 0.80f;
        [PrototypeOnly(PendingDecisions.None, "Hysteresis: seconds of contact before friction applies / seconds after the last contact before it releases.", true)]
        public float WallEnterSeconds = 0.10f, WallExitSeconds = 0.35f;

        [Header("Gate threshold push")]
        [PrototypeOnly(PendingDecisions.None, "SeparationMass multiplier inside a Gate volume: heavy tiers (Elite / Ultra) and light tiers (Standard / Special).", true)]
        public float GateMassHeavy = 1.5f, GateMassLight = 0.6f;

        [Header("Queue / wait variety")]
        [PrototypeOnly(PendingDecisions.None, "Stall threshold band rolled per life (WP-050 UpdateWaiting; mean 0.65 s).", true)]
        public float StallSecondsMin = 0.45f, StallSecondsMax = 0.85f;
        [PrototypeOnly(PendingDecisions.None, "Queued light bodies sway laterally: speed (m/s), period band (s), amplitude cap (m).", true)]
        public float ShuffleSpeed = 0.08f, ShufflePeriodMin = 1.4f, ShufflePeriodMax = 2.2f, ShuffleAmplitude = 0.12f;

        [Header("Corpse linger")]
        [PrototypeOnly(PendingDecisions.None, "WP-076 corpse lifetime v2 (DEC-096, aligned with S-06 A3.1/A3.6): seconds a corpse stays after its death reaction at Yüksek, per tier (Standard, Special, Elite, Ultra).", true)]
        public float[] CorpseLinger = { 4.0f, 4.0f, 5.0f, 6.0f };
        [PrototypeOnly(PendingDecisions.None, "WP-076: corpse linger scale by quality tier (Performans, Yüksek, Sinematik) and the corpse ceiling (S-06 6 / 10 / 16; the oldest is buried first).", true)]
        public float[] CorpseQualityScale = { 0.6f, 1.0f, 1.25f };
        public int[] CorpseCeiling = { 6, 10, 16 };
        [PrototypeOnly(PendingDecisions.None, "Per-corpse jitter (fraction) so a burst of kills does not vanish in one frame.", true)]
        [Range(0f, 0.5f)] public float CorpseJitter = 0.15f;
        [PrototypeOnly(PendingDecisions.None, "Near the body ceiling (spawned ≥ capacity − CorpseCapMargin) the linger is scaled by this; the Ultra keeps at least UltraCorpseFloor.", true)]
        public float CorpseCapScale = 0.4f, UltraCorpseFloor = 1.2f;
        public int CorpseCapMargin = 2;

        [Header("Threat-aware spawn visibility")]
        [PrototypeOnly(PendingDecisions.None, "On-screen spawn weight factor by Threat Director layer 0..3 (replaces SpawnWeightTuning.OnScreenFactor 0.35 as the layer rises).", true)]
        public float[] OnScreenFactorByThreatLayer = { 0.35f, 0.35f, 0.20f, 0.10f };

        private static CrowdFeelSettings s_active;
        public static CrowdFeelSettings Active
        {
            get
            {
                if (s_active == null)
                {
                    s_active = Resources.Load<CrowdFeelSettings>(ResourcesPath);
                    if (s_active == null)
                    {
                        s_active = CreateInstance<CrowdFeelSettings>();
                        s_active.name = "DD_CrowdFeel (code defaults)";
                        s_active.hideFlags = HideFlags.HideAndDontSave;
                    }
                }
                return s_active;
            }
        }
        public static void SetActive(CrowdFeelSettings settings) => s_active = settings;
        public static void ResetStatics() => s_active = null;

        public float FlankShareOf(EnemyTier tier) => FlankShare != null && (int)tier < FlankShare.Length ? Mathf.Clamp01(FlankShare[(int)tier]) : 0f;
        public bool IsHeavy(EnemyTier tier) => tier == EnemyTier.Elite || tier == EnemyTier.Ultra;
        public float GateMassFor(EnemyTier tier) => IsHeavy(tier) ? GateMassHeavy : GateMassLight;

        /// <summary>The corpse linger for a kill: tier base × jitter, scaled near the ceiling (Ultra floor kept).</summary>
        public float CorpseLingerFor(EnemyTier tier, float jitter01, bool nearCeiling)
        {
            return CorpseLingerFor(tier, jitter01, nearCeiling, QualityTier.Yuksek);
        }

        /// <summary>WP-076: × the quality tier's scale before the ceiling rule (the Ultra floor is not scaled).</summary>
        public float CorpseLingerFor(EnemyTier tier, float jitter01, bool nearCeiling, QualityTier quality)
        {
            float baseSeconds = CorpseLinger != null && (int)tier < CorpseLinger.Length ? CorpseLinger[(int)tier] : 4.0f;
            baseSeconds *= CorpseQualityScaleFor(quality);
            float seconds = baseSeconds * (1f + (Mathf.Clamp01(jitter01) * 2f - 1f) * CorpseJitter);
            if (nearCeiling) seconds = Mathf.Max(seconds * CorpseCapScale, tier == EnemyTier.Ultra ? UltraCorpseFloor : 0f);
            return Mathf.Max(0.05f, seconds);
        }

        public float CorpseQualityScaleFor(QualityTier quality) => CorpseQualityScale != null && (int)quality < CorpseQualityScale.Length ? Mathf.Max(0.05f, CorpseQualityScale[(int)quality]) : 1f;
        public int CorpseCeilingFor(QualityTier quality) => CorpseCeiling != null && (int)quality < CorpseCeiling.Length ? Mathf.Max(1, CorpseCeiling[(int)quality]) : 10;

        public float OnScreenFactorFor(int threatLayer, float fallback)
        {
            if (OnScreenFactorByThreatLayer == null || OnScreenFactorByThreatLayer.Length == 0) return fallback;
            int i = Mathf.Clamp(threatLayer, 0, OnScreenFactorByThreatLayer.Length - 1);
            return Mathf.Clamp(OnScreenFactorByThreatLayer[i], 0.01f, 1f);
        }

        /// <summary>
        /// The flank goal: the target shifted sideways by the per-life sign, an offset that grows with distance
        /// (0 inside FlankStraightRadius, full past +FlankBlendMetres) and follows the approach angle. Pure.
        /// </summary>
        public Vector3 FlankGoal(Vector3 self, Vector3 target, int sign, float angleDegrees)
        {
            if (sign == 0) return target;
            Vector3 to = target - self; to.y = 0f;
            float distance = to.magnitude;
            if (distance < 1e-3f) return target;
            float t = Mathf.Clamp01((distance - FlankStraightRadius) / Mathf.Max(0.01f, FlankBlendMetres));
            if (t <= 0f) return target;
            Vector3 dir = to / distance;
            Vector3 side = Vector3.Cross(Vector3.up, dir) * sign;
            float offset = Mathf.Min(FlankOffsetMax, Mathf.Tan(angleDegrees * Mathf.Deg2Rad) * Mathf.Min(distance, 4f)) * t;
            return target + side * offset;
        }
    }

    /// <summary>
    /// WP-054: the Threat Director's layer, published in Core-facing form so the spawn director (Spawn assembly
    /// boundary: no Enemies reference) can weigh on-screen points down as the threat rises. Written by
    /// ThreatDirector.Sample; -1 = no sample yet.
    /// </summary>
    public static class ThreatLevelFeed
    {
        public static int Layer { get; private set; } = -1;
        public static void Publish(int layer) => Layer = layer;
        public static void ResetStatics() => Layer = -1;
    }
}
