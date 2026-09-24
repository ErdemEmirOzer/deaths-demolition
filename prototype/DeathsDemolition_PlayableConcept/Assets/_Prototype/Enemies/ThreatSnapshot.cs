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
        public int LifeId;       // WP-076 §6: the nearest rear body's identity for the HUD / audio
        public bool OutOfView;   // WP-076 §6: !CameraView.IsInView(chest, mode margin) — read only when ThreatContext.UseViewFlags
    }

    /// <summary>WP-076 §6: how loudly the rear is announced (S-08 sound, S-09 HUD). None / Aware (breath, no HUD) / Warn (arc) / Alert (hard).</summary>
    public enum ThreatRearLevel
    {
        None = 0,
        Aware = 1,
        Warn = 2,
        Alert = 3
    }

    /// <summary>The player-side inputs of one evaluation.</summary>
    public struct ThreatContext
    {
        public Vector3 PlayerPosition;
        public Vector3 ViewForward;      // camera / aim forward on the XZ plane
        public float Health01;           // 0..1
        public float RoundProgress01;    // pool spent share of the current round
        public bool InFight;             // RunPhase.Fighting; false between rounds / dead / menu (DEC-065 pull-back)
        public int Mode;                 // WP-076 §6: CameraMode (0 TopDown, 1 ThirdPerson, 2 FirstPerson)
        public bool UseViewFlags;        // WP-076 §6: a camera rig answered IsInView (else the per-mode fallback angle)
    }

    /// <summary>
    /// WP-033 (ADR-033-1) · The threat signal: a smoothed 0..1 intensity, its components, the
    /// music layer with hysteresis (DEC-065) and the warning flags (REQ-105). Read-only value;
    /// consumers (music, ambience, HUD, game feel) never write back.
    /// </summary>
    public readonly struct ThreatSnapshot
    {
        public ThreatSnapshot(float intensity, float raw, int layer, float proximity, float encirclement, float tier, float round, float danger, float attackPrep,
                              int nearCount, int occupiedSectors, int windingUp, EnemyTier strongestTier, bool encircled, bool rearAttack, bool lowHealth, bool strongVersion, bool inFight, int enemyCount,
                              Vector3 rearDirection = default, float rearIntensity = 0f, float rearBearingDegrees = 0f, int rearCount = 0,
                              float rearNearestDistance = -1f, EnemyTier rearNearestTier = EnemyTier.Standard, int rearNearestLifeId = 0, bool rearNearestWindingUp = false,
                              ThreatRearLevel rearLevel = ThreatRearLevel.None, int mode = 0)
        {
            RearNearestDistance = rearNearestDistance; RearNearestTier = rearNearestTier; RearNearestLifeId = rearNearestLifeId;
            RearNearestWindingUp = rearNearestWindingUp; RearLevel = rearLevel; Mode = mode;
            RearDirection = rearDirection; RearIntensity = rearIntensity; RearBearingDegrees = rearBearingDegrees; RearCount = rearCount;
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

        /// <summary>
        /// WP-068 U12b · the rear-arc reading (data only, S-09 HUD arc): the danger-weighted direction of the near
        /// enemies OUTSIDE the view cone (same <c>RearAngleDegrees</c> as <see cref="RearAttack"/>), world XZ unit
        /// vector from the player; zero when nothing presses from behind. Weight per body = closeness (1 at the
        /// player → 0 at NearRadius) × tier (1 / 1.3 / 1.6 / 2) × 2 while winding up.
        /// </summary>
        public Vector3 RearDirection { get; }
        /// <summary>0..1: summed rear weight / <see cref="ThreatModel.RearFullWeight"/> (one close winding-up Standard ≈ 0.5).</summary>
        public float RearIntensity { get; }
        /// <summary>Signed angle from the view forward to <see cref="RearDirection"/>, degrees (−180..180, + = clockwise from above / to the right).</summary>
        public float RearBearingDegrees { get; }
        /// <summary>Near bodies behind the view this frame.</summary>
        public int RearCount { get; }

        /// <summary>WP-076 §6: distance (m) of the nearest body behind the view within the re-arm range (−1 = none).</summary>
        public float RearNearestDistance { get; }
        public EnemyTier RearNearestTier { get; }
        public int RearNearestLifeId { get; }
        public bool RearNearestWindingUp { get; }
        /// <summary>WP-076 §6: the announced rear level (per-mode thresholds, per-mode hold; TopDown never Aware).</summary>
        public ThreatRearLevel RearLevel { get; }
        /// <summary>The camera mode this snapshot was evaluated for.</summary>
        public int Mode { get; }

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
        public ThreatRearLevel RearLevel;   // WP-076 §6
        public float RearLevelHold;
        public int Mode;
    }

    /// <summary>
    /// The pure model: inputs + settings + previous state + dt → snapshot. Deterministic,
    /// allocation-free (fixed sector array on the stack), no Unity object access.
    /// </summary>
    public static class ThreatModel
    {
        /// <summary>WP-068 U12b: summed rear weight that reads as full intensity (PROTOTYPE-ONLY; HUD read only, no rule uses it).</summary>
        public const float RearFullWeight = 4f;
        private static float RearTierWeight(EnemyTier t) => t == EnemyTier.Ultra ? 2f : t == EnemyTier.Elite ? 1.6f : t == EnemyTier.Special ? 1.3f : 1f;

        public static ThreatSnapshot Evaluate(ThreatSettings s, in ThreatContext ctx, ThreatEnemyInput[] enemies, int count, ref ThreatState state, float dt)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            Span<bool> sectors = stackalloc bool[ThreatSettings.SectorCount];
            int near = 0, occupied = 0, windingUp = 0;
            bool rear = false;
            float rearX = 0f, rearZ = 0f, rearWeight = 0f; int rearCount = 0;   // WP-068 U12b
            EnemyTier strongest = EnemyTier.Standard;
            float nearR2 = s.NearRadius * s.NearRadius, prepR2 = s.AttackPrepRadius * s.AttackPrepRadius;
            Vector2 fwd = new Vector2(ctx.ViewForward.x, ctx.ViewForward.z);
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector2.up; else fwd.Normalize();
            // WP-076 §6: per-mode rear test — out of view (camera rig) AND in the rear half-plane; without a rig the mode's angle
            float rearCos = Mathf.Cos((ctx.Mode == 0 ? s.RearAngleDegrees : s.ByMode(s.RearAngleByMode, ctx.Mode, s.RearAngleDegrees)) * Mathf.Deg2Rad);
            if (state.Mode != ctx.Mode) { state.Mode = ctx.Mode; state.RearHold = 0f; state.RearLevelHold = 0f; state.RearLevel = ThreatRearLevel.None; }
            float nearestRear = float.MaxValue; EnemyTier nearestTier = EnemyTier.Standard; int nearestLife = 0; bool nearestWinding = false;
            float pairR = s.ByMode(s.RearWarnPairMetres, ctx.Mode, 0f); int pairCount = 0;
            float rearScan2 = s.RearBeatRearmMetres * s.RearBeatRearmMetres;

            for (int i = 0; i < count; i++)
            {
                ref ThreatEnemyInput e = ref enemies[i];
                float dx = e.Position.x - ctx.PlayerPosition.x, dz = e.Position.z - ctx.PlayerPosition.z;
                float d2 = dx * dx + dz * dz;
                if ((int)e.Tier > (int)strongest) strongest = e.Tier;
                bool isRear = false;
                if (d2 <= rearScan2 || d2 <= nearR2 || (e.WindingUp && d2 <= prepR2))
                {
                    float dd = Mathf.Sqrt(d2);
                    float dot = dd > 1e-4f ? (dx * fwd.x + dz * fwd.y) / dd : 1f;
                    isRear = dd > 1e-4f && (ctx.UseViewFlags ? e.OutOfView && dot < 0f : dot < rearCos);
                    if (isRear && d2 <= rearScan2)
                    {
                        if (dd < nearestRear) { nearestRear = dd; nearestTier = e.Tier; nearestLife = e.LifeId; nearestWinding = e.WindingUp; }
                        if (pairR > 0f && dd <= pairR) pairCount++;
                    }
                }
                if (d2 <= nearR2)
                {
                    near++;
                    // WP-068 U12b: outside the view cone → contributes to the rear-arc direction (data only)
                    float dist = Mathf.Sqrt(d2);
                    if (isRear)
                    {
                        float w = (1f - Mathf.Clamp01(dist / Mathf.Max(0.01f, s.NearRadius))) * RearTierWeight(e.Tier) * (e.WindingUp ? 2f : 1f);
                        rearX += dx / dist * w; rearZ += dz / dist * w; rearWeight += w; rearCount++;
                    }
                    int sector = (int)((Mathf.Atan2(dz, dx) + Mathf.PI) / (2f * Mathf.PI) * ThreatSettings.SectorCount) % ThreatSettings.SectorCount;
                    if (sector < 0) sector += ThreatSettings.SectorCount;
                    if (!sectors[sector]) { sectors[sector] = true; occupied++; }
                }

                if (e.WindingUp && d2 <= prepR2)
                {
                    windingUp++;
                    if (isRear) rear = true;
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
            float hold = ctx.Mode == 0 ? s.FlagHoldSeconds : s.ByMode(s.RearHoldSeconds, ctx.Mode, s.FlagHoldSeconds);
            state.RearHold = rear ? hold : Mathf.Max(0f, state.RearHold - dt);
            bool encircled = encircledNow || state.EncircledHold > 0f;
            bool rearAttack = rear || state.RearHold > 0f;
            bool lowHealth = ctx.Health01 <= s.LowHealthThreshold;
            bool strong = strongest == EnemyTier.Elite || strongest == EnemyTier.Ultra;

            Vector3 rearDir = Vector3.zero; float bearing = 0f;
            float rl = Mathf.Sqrt(rearX * rearX + rearZ * rearZ);
            if (rl > 1e-4f)
            {
                rearDir = new Vector3(rearX / rl, 0f, rearZ / rl);
                bearing = Vector2.SignedAngle(new Vector2(rearDir.x, rearDir.z), fwd);   // + = to the right of the view (clockwise from above)
            }
            float rearIntensity = Mathf.Clamp01(rearWeight / RearFullWeight);

            // WP-076 §6: the announced rear level (per mode) with a hold so it never blinks
            bool hasNearest = nearestRear < float.MaxValue;
            float aware = s.ByMode(s.RearAwareMetres, ctx.Mode, 0f), warnR = s.ByMode(s.RearWarnMetres, ctx.Mode, 0f), alertR = s.ByMode(s.RearAlertMetres, ctx.Mode, 0f);
            bool warnNow = rearIntensity >= s.ByMode(s.RearWarnIntensity, ctx.Mode, 0.4f) || (hasNearest && warnR > 0f && nearestRear <= warnR) || (pairR > 0f && pairCount >= 2);
            bool alertNow = rearAttack || (hasNearest && alertR > 0f && nearestRear <= alertR);
            bool awareNow = hasNearest && aware > 0f && nearestRear <= aware;
            ThreatRearLevel want = alertNow ? ThreatRearLevel.Alert : warnNow ? ThreatRearLevel.Warn : awareNow ? ThreatRearLevel.Aware : ThreatRearLevel.None;
            if (want >= state.RearLevel) { state.RearLevel = want; if (want != ThreatRearLevel.None) state.RearLevelHold = hold; }
            else
            {
                state.RearLevelHold = Mathf.Max(0f, state.RearLevelHold - dt);
                if (state.RearLevelHold <= 0f) { state.RearLevel = want; state.RearLevelHold = want != ThreatRearLevel.None ? hold : 0f; }
            }

            return new ThreatSnapshot(smoothed, raw, layer, proximity, encirclement, tier, round, danger, prep, near, occupied, windingUp, strongest, encircled, rearAttack, lowHealth, strong, ctx.InFight, count,
                rearDir, rearIntensity, bearing, rearCount,
                hasNearest ? nearestRear : -1f, nearestTier, nearestLife, nearestWinding, state.RearLevel, ctx.Mode);
        }

        private static float ThresholdUp(ThreatSettings s, int layer) => Threshold(s, layer) + s.LayerHysteresis;
        private static float ThresholdDown(ThreatSettings s, int layer) => Threshold(s, layer) - s.LayerHysteresis;
        private static float Threshold(ThreatSettings s, int layer) => layer <= 1 ? s.Layer1Threshold : layer == 2 ? s.Layer2Threshold : s.Layer3Threshold;
    }
}
