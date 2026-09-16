using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>One enemy as the threat model sees it (gathered per frame, no references kept).</summary>
    public struct ThreatEnemyInput
    {
        public Vector3 Position;
        public EnemyTier Tier;
        public bool WindingUp;   // attack telegraph running (ZombieAttackPhase.Windup)
    }

    /// <summary>The player-side inputs of one evaluation.</summary>
    public struct ThreatContext
    {
        public Vector3 PlayerPosition;
        public Vector3 ViewForward;      // camera / aim forward on the XZ plane
        public float Health01;           // 0..1
        public float RoundProgress01;    // pool spent share of the current round
        public bool InFight;             // RunPhase.Fighting; false between rounds / dead / menu (DEC-065 pull-back)
    }

    /// <summary>
    /// WP-033 (ADR-033-1) · The threat signal: a smoothed 0..1 intensity, its components, the
    /// music layer with hysteresis (DEC-065) and the warning flags (REQ-105). Read-only value;
    /// consumers (music, ambience, HUD, game feel) never write back.
    /// </summary>
    public readonly struct ThreatSnapshot
    {
        public ThreatSnapshot(float intensity, float raw, int layer, float proximity, float encirclement, float tier, float round, float danger, float attackPrep,
                              int nearCount, int occupiedSectors, int windingUp, EnemyTier strongestTier, bool encircled, bool rearAttack, bool lowHealth, bool strongVersion, bool inFight, int enemyCount)
        {
            Intensity = intensity; Raw = raw; Layer = layer;
            Proximity = proximity; Encirclement = encirclement; TierPresence = tier; RoundProgress = round; PlayerDanger = danger; AttackPrep = attackPrep;
            NearCount = nearCount; OccupiedSectors = occupiedSectors; WindingUp = windingUp; StrongestTier = strongestTier;
            Encircled = encircled; RearAttack = rearAttack; LowHealth = lowHealth; StrongVersionPresent = strongVersion; InFight = inFight; EnemyCount = enemyCount;
        }

        /// <summary>Smoothed, hysteresis-free 0..1 driver (music tension / percussion, ambience density).</summary>
        public float Intensity { get; }
        /// <summary>Unsmoothed composition of the components this frame.</summary>
        public float Raw { get; }
        /// <summary>Music layer 0..3 with hysteresis (DEC-065: never jumps on a flicker).</summary>
        public int Layer { get; }

        public float Proximity { get; }
        public float Encirclement { get; }
        public float TierPresence { get; }
        public float RoundProgress { get; }
        public float PlayerDanger { get; }
        public float AttackPrep { get; }

        public int NearCount { get; }
        public int OccupiedSectors { get; }
        public int WindingUp { get; }
        public EnemyTier StrongestTier { get; }

        /// <summary>REQ-105 / DEC-032: ≥ EncircleMinSectors of 8 sectors hold a near enemy.</summary>
        public bool Encircled { get; }
        /// <summary>REQ-105: an attack is winding up behind the view (outside the front half-plane band).</summary>
        public bool RearAttack { get; }
        public bool LowHealth { get; }
        /// <summary>DEC-067: an Elite or Ultra is alive.</summary>
        public bool StrongVersionPresent { get; }
        public bool InFight { get; }
        public int EnemyCount { get; }

        public override string ToString() =>
            "threat " + Intensity.ToString("0.00") + " (raw " + Raw.ToString("0.00") + ") layer " + Layer + " near " + NearCount + " sectors " + OccupiedSectors + "/8" +
            " tier " + StrongestTier + " windup " + WindingUp + (Encircled ? " ENCIRCLED" : "") + (RearAttack ? " REAR" : "") + (LowHealth ? " LOWHP" : "") + (InFight ? "" : " (between rounds)");
    }

    /// <summary>Persistent state of the model between evaluations (smoothing, hysteresis, flag hold).</summary>
    public struct ThreatState
    {
        public float Intensity;
        public int Layer;
        public float EncircledHold;
        public float RearHold;
        public bool Initialised;
    }

    /// <summary>
    /// The pure model: inputs + settings + previous state + dt → snapshot. Deterministic,
    /// allocation-free (fixed sector array on the stack), no Unity object access.
    /// </summary>
    public static class ThreatModel
    {
        public static ThreatSnapshot Evaluate(ThreatSettings s, in ThreatContext ctx, ThreatEnemyInput[] enemies, int count, ref ThreatState state, float dt)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            Span<bool> sectors = stackalloc bool[ThreatSettings.SectorCount];
            int near = 0, occupied = 0, windingUp = 0;
            bool rear = false;
            EnemyTier strongest = EnemyTier.Standard;
            float nearR2 = s.NearRadius * s.NearRadius, prepR2 = s.AttackPrepRadius * s.AttackPrepRadius;
            Vector2 fwd = new Vector2(ctx.ViewForward.x, ctx.ViewForward.z);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector2.up; else fwd.Normalize();
            float rearCos = Mathf.Cos(s.RearAngleDegrees * Mathf.Deg2Rad);

            for (int i = 0; i < count; i++)
            {
                ref ThreatEnemyInput e = ref enemies[i];
                float dx = e.Position.x - ctx.PlayerPosition.x, dz = e.Position.z - ctx.PlayerPosition.z;
                float d2 = dx * dx + dz * dz;
                if ((int)e.Tier > (int)strongest) strongest = e.Tier;
                if (d2 <= nearR2)
                {
                    near++;
                    int sector = (int)((Mathf.Atan2(dz, dx) + Mathf.PI) / (2f * Mathf.PI) * ThreatSettings.SectorCount) % ThreatSettings.SectorCount;
                    if (sector < 0) sector += ThreatSettings.SectorCount;
                    if (!sectors[sector]) { sectors[sector] = true; occupied++; }
                }

                if (e.WindingUp && d2 <= prepR2)
                {
                    windingUp++;
                    float len = Mathf.Sqrt(d2);
                    if (len > 1e-4f)
                    {
                        float cos = (dx * fwd.x + dz * fwd.y) / len;
                        if (cos < rearCos) rear = true;
                    }
                }
            }

            float proximity = Mathf.Clamp01(s.ProximityFullCount > 0 ? (float)near / s.ProximityFullCount : 0f);
            float encirclement = Mathf.Clamp01((float)occupied / ThreatSettings.SectorCount);
            float tier = strongest == EnemyTier.Ultra ? s.UltraPresence : strongest == EnemyTier.Elite ? s.ElitePresence : strongest == EnemyTier.Special ? s.SpecialPresence : 0f;
            float round = Mathf.Clamp01(ctx.RoundProgress01);
            float danger = Mathf.Clamp01(1f - ctx.Health01);
            float prep = Mathf.Clamp01(s.AttackPrepFullCount > 0 ? (float)windingUp / s.AttackPrepFullCount : 0f);

            float raw = Mathf.Clamp01(proximity * s.ProximityWeight + encirclement * s.EncirclementWeight + tier * s.TierWeight + round * s.RoundWeight + danger * s.DangerWeight + prep * s.AttackPrepWeight);
            if (!ctx.InFight) raw = Mathf.Min(raw, s.BetweenRoundsCeiling);   // DEC-065 pull-back between rounds

            // asymmetric smoothing: attacks fast, releases slow; the ceiling between rounds is a hard clamp
            float smoothed;
            if (!state.Initialised) { smoothed = raw; state.Initialised = true; }
            else
            {
                float rate = raw > state.Intensity ? s.RiseRate : s.FallRate;
                smoothed = Mathf.MoveTowards(state.Intensity, raw, Mathf.Max(0f, rate) * Mathf.Max(0f, dt));
            }

            if (!ctx.InFight) smoothed = Mathf.Min(smoothed, s.BetweenRoundsCeiling);
            state.Intensity = smoothed;

            // layer with hysteresis
            int layer = state.Layer;
            if (layer < 3 && smoothed >= ThresholdUp(s, layer + 1)) layer++;
            else if (layer > 0 && smoothed < ThresholdDown(s, layer)) layer--;
            if (!ctx.InFight && layer > 0 && smoothed < ThresholdDown(s, layer)) layer = 0;
            state.Layer = layer;

            // flags with a short hold so a one-frame gap does not blink the warning
            bool encircledNow = occupied >= s.EncircleMinSectors;
            state.EncircledHold = encircledNow ? s.FlagHoldSeconds : Mathf.Max(0f, state.EncircledHold - dt);
            state.RearHold = rear ? s.FlagHoldSeconds : Mathf.Max(0f, state.RearHold - dt);
            bool encircled = encircledNow || state.EncircledHold > 0f;
            bool rearAttack = rear || state.RearHold > 0f;
            bool lowHealth = ctx.Health01 <= s.LowHealthThreshold;
            bool strong = strongest == EnemyTier.Elite || strongest == EnemyTier.Ultra;

            return new ThreatSnapshot(smoothed, raw, layer, proximity, encirclement, tier, round, danger, prep, near, occupied, windingUp, strongest, encircled, rearAttack, lowHealth, strong, ctx.InFight, count);
        }

        private static float ThresholdUp(ThreatSettings s, int layer) => Threshold(s, layer) + s.LayerHysteresis;
        private static float ThresholdDown(ThreatSettings s, int layer) => Threshold(s, layer) - s.LayerHysteresis;
        private static float Threshold(ThreatSettings s, int layer) => layer <= 1 ? s.Layer1Threshold : layer == 2 ? s.Layer2Threshold : s.Layer3Threshold;
    }
}
