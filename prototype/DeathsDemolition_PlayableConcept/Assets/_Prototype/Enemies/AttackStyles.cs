using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-076 (AI v3, direction 8980fe9 §1) · the five attack styles as data. PROTOTYPE-ONLY numbers from the
    /// game-designer council; the tier's config values (DEC-021 telegraph / damage / cooldown) stay the reference —
    /// each style is a multiplier normalised to the tier's SIGNATURE style (Standard Swipe, Special Lunge, Elite Sweep,
    /// Ultra Overhead = 1.00), so a tier's own attack is unchanged and the others scale from it.
    /// </summary>
    public static class AttackStyleProfile
    {
        public const int StyleCount = 5;

        [PrototypeOnly(PendingDecisions.None, "WP-076 §1.1 style multipliers (index = ZombieAttackStyle): telegraph, damage, cooldown, hit radius × BodyRadius, arc half-angle (deg).", true)]
        public static readonly float[] TelegraphMul = { 1.00f, 0.85f, 1.45f, 2.10f, 1.30f };
        public static readonly float[] DamageMul = { 1.00f, 0.90f, 1.80f, 3.40f, 1.60f };
        public static readonly float[] CooldownMul = { 1.00f, 1.00f, 1.20f, 1.50f, 1.60f };
        public static readonly float[] HitRadiusFactor = { 1.7f, 1.9f, 2.1f, 1.8f, 1.2f };
        public static readonly float[] ArcDegrees = { 35f, 25f, 75f, 20f, 15f };

        [PrototypeOnly(PendingDecisions.None, "WP-076 §1.2 tier × style weights (row = EnemyTier, column = ZombieAttackStyle).", true)]
        public static readonly float[,] TierWeight =
        {
            { 0.55f, 0.05f, 0.10f, 0.00f, 0.30f },   // Standard
            { 0.40f, 0.60f, 0.00f, 0.00f, 0.00f },   // Special (Runner)
            { 0.00f, 0.00f, 0.45f, 0.25f, 0.30f },   // Elite (Guard)
            { 0.00f, 0.00f, 0.40f, 0.60f, 0.00f },   // Ultra (Executioner)
        };

        [PrototypeOnly(PendingDecisions.None, "WP-076 §1.1 reach: Lunge band (Standard band end), BiteGrab max, Elite Overhead share of range; §1.3 falloff outside a band.", true)]
        public const float LungeMinMetres = 1.8f, LungeMaxMetres = 2.6f, LungeMaxMetresStandard = 2.2f, BiteGrabMaxMetres = 0.8f,
                           EliteOverheadRangeShare = 0.8f, RangeFalloffShare = 0.2f, AngleSoftDegrees = 15f,
                           SweepSideMinDegrees = 30f, SweepSideMaxDegrees = 70f, SweepSideBonus = 1.3f,
                           PlayerBackTurnedDegrees = 110f, PlayerStillSpeed = 1.0f, StandardLungeReach = 0.8f;

        public static ZombieAttackStyle Signature(EnemyTier tier) =>
            tier == EnemyTier.Special ? ZombieAttackStyle.Lunge : tier == EnemyTier.Elite ? ZombieAttackStyle.Sweep
            : tier == EnemyTier.Ultra ? ZombieAttackStyle.Overhead : ZombieAttackStyle.Swipe;

        /// <summary>A tier's base value (its signature style) → the value for <paramref name="style"/>.</summary>
        public static float Scale(float[] table, EnemyTier tier, ZombieAttackStyle style, float tierBase) =>
            tierBase * table[(int)style] / table[(int)Signature(tier)];

        public static float Weight(EnemyTier tier, ZombieAttackStyle style) => TierWeight[Mathf.Clamp((int)tier, 0, 3), (int)style];

        /// <summary>Upper reach of a style for this tier (the lower bound is 0 except for the Lunge band).</summary>
        public static float MaxReach(EnemyTier tier, ZombieAttackStyle style, float attackRange)
        {
            switch (style)
            {
                case ZombieAttackStyle.Lunge: return tier == EnemyTier.Standard ? LungeMaxMetresStandard : LungeMaxMetres;
                case ZombieAttackStyle.BiteGrab: return Mathf.Min(BiteGrabMaxMetres, attackRange * 0.65f);
                case ZombieAttackStyle.Overhead: return tier == EnemyTier.Elite ? attackRange * EliteOverheadRangeShare : attackRange;
                default: return attackRange;
            }
        }

        /// <summary>The furthest any allowed style of this tier can commit from — the body starts selecting inside it.</summary>
        public static float SelectionReach(EnemyTier tier, float attackRange)
        {
            float reach = 0f;
            for (int s = 0; s < StyleCount; s++)
                if (Weight(tier, (ZombieAttackStyle)s) > 0f) reach = Mathf.Max(reach, MaxReach(tier, (ZombieAttackStyle)s, attackRange));
            return reach;
        }
    }

    /// <summary>What the selector knows about one body at the moment it may swing (value type, no references).</summary>
    public struct AttackContext
    {
        public EnemyTier Tier;
        public float Distance;               // flat metres to the player
        public float AngleToPlayer;          // degrees between the body's forward and the player (0 = straight ahead), signed (+ = player to the right)
        public float PlayerFacingAway;       // degrees between the player's forward and the direction from the player to this body (> 110 = back turned)
        public float PlayerSpeed;            // m/s
        public float AttackRange;            // the tier's AttackRange
        public int LastStyle, SecondLastStyle;   // -1 = none
        public bool LastSwipeMirrored;       // last swipe used the left hand
        public float Now;
    }

    /// <summary>
    /// WP-076 §1.3 · pure, deterministic choice of the next attack. Score = tier weight × range fit × angle fit ×
    /// variety × slot; weighted draw from the body's own stream (spawn seed). Returns false when nothing may swing
    /// (the body keeps pressing). 0 allocation (stackalloc).
    /// </summary>
    public static class AttackSelector
    {
        /// <summary>WP-076 `-ddAiV3 0`: every body uses only its tier's signature style and no slot board (the WP-040 behaviour; A/B).</summary>
        public static bool Enabled { get; set; } = true;

        public static bool Select(in AttackContext c, ref EnemyRandom random, out ZombieAttackStyle style, out bool mirror)
        {
            mirror = false;
            style = AttackStyleProfile.Signature(c.Tier);
            if (!Enabled)
            {
                return c.Distance <= c.AttackRange;
            }

            Span<float> score = stackalloc float[AttackStyleProfile.StyleCount];
            float total = 0f;
            for (int i = 0; i < AttackStyleProfile.StyleCount; i++)
            {
                var s = (ZombieAttackStyle)i;
                float w = AttackStyleProfile.Weight(c.Tier, s);
                if (w <= 0f) { score[i] = 0f; continue; }
                w *= RangeFit(in c, s) * AngleFit(in c, s) * Variety(in c, i);
                if (w > 0f && s == ZombieAttackStyle.BiteGrab && !BiteGrabAllowed(in c)) w = 0f;
                if (w > 0f && !AttackSlotBoard.CanAcquire(s, c.Now)) w = 0f;
                score[i] = w;
                total += w;
            }

            if (total <= 0f) return false;
            float roll = random.Next01() * total;
            for (int i = 0; i < AttackStyleProfile.StyleCount; i++)
            {
                if (score[i] <= 0f) continue;
                if (roll < score[i] || i == AttackStyleProfile.StyleCount - 1) { style = (ZombieAttackStyle)i; break; }
                roll -= score[i];
            }
            if (score[(int)style] <= 0f)
            {
                for (int i = AttackStyleProfile.StyleCount - 1; i >= 0; i--) if (score[i] > 0f) { style = (ZombieAttackStyle)i; break; }
            }

            if (style == ZombieAttackStyle.Swipe) mirror = SwipeHand(in c);
            return true;
        }

        /// <summary>
        /// Hard gate: 0 outside [min, MaxReach] — a body never commits a swing it cannot land (review R1-A #9; the Standard
        /// swings at ≤ 1.7 m as before). Inside, the last <see cref="AttackStyleProfile.RangeFalloffShare"/> of the reach
        /// tapers the weight to <see cref="EdgeFit"/> so the mix favours the style whose reach fits best.
        /// </summary>
        public static float RangeFit(in AttackContext c, ZombieAttackStyle s)
        {
            float max = AttackStyleProfile.MaxReach(c.Tier, s, c.AttackRange);
            float min = s == ZombieAttackStyle.Lunge ? AttackStyleProfile.LungeMinMetres : 0f;
            if (c.Distance > max || c.Distance < min) return 0f;
            float soft = Mathf.Max(0.05f, max * AttackStyleProfile.RangeFalloffShare);
            float edge = Mathf.Min(max - c.Distance, s == ZombieAttackStyle.Lunge ? c.Distance - min : soft);
            return edge >= soft ? 1f : Mathf.Lerp(EdgeFit, 1f, edge / soft);
        }

        /// <summary>Weight at the very edge of a style's band (the mix still allows it; it never zeroes a valid swing).</summary>
        public const float EdgeFit = 0.35f;

        public static float AngleFit(in AttackContext c, ZombieAttackStyle s)
        {
            float a = Mathf.Abs(c.AngleToPlayer), arc = AttackStyleProfile.ArcDegrees[(int)s];
            float fit = a <= arc ? 1f : a <= arc + AttackStyleProfile.AngleSoftDegrees ? 0.5f : 0f;
            if (s == ZombieAttackStyle.Sweep && a >= AttackStyleProfile.SweepSideMinDegrees && a <= AttackStyleProfile.SweepSideMaxDegrees) fit *= AttackStyleProfile.SweepSideBonus;
            return fit;
        }

        /// <summary>Same style as last → ×0.25; the last two → 0 (never three in a row).</summary>
        public static float Variety(in AttackContext c, int style)
        {
            if (c.LastStyle == style && c.SecondLastStyle == style) return 0f;
            return c.LastStyle == style ? 0.25f : 1f;
        }

        /// <summary>§1.4: player turned away or standing still; nobody else holding the player.</summary>
        public static bool BiteGrabAllowed(in AttackContext c) =>
            !GrabState.Active && (c.PlayerFacingAway > AttackStyleProfile.PlayerBackTurnedDegrees || c.PlayerSpeed < AttackStyleProfile.PlayerStillSpeed);

        /// <summary>Left hand (mirror) when the player is on the body's left, right hand on its right; never the same hand twice in a row.</summary>
        public static bool SwipeHand(in AttackContext c)
        {
            bool left = c.AngleToPlayer < -5f ? true : c.AngleToPlayer > 5f ? false : !c.LastSwipeMirrored;
            if (c.LastStyle == (int)ZombieAttackStyle.Swipe && left == c.LastSwipeMirrored) left = !left;
            return left;
        }
    }

    /// <summary>
    /// WP-076 §1.5 · crowd coordination: per-style concurrent slots (held Begin → end / cancel / death), a cooldown per
    /// style after a commit (global), a total Windup cap (2 while the player is grabbed) and a small spread between
    /// Swipe wind-ups. Static fixed arrays, 0 allocation.
    /// </summary>
    public static class AttackSlotBoard
    {
        [PrototypeOnly(PendingDecisions.None, "WP-076 §1.5 per-style slots, per-style cooldown after a commit (s), total Windup cap (grab active cap), Swipe wind-up spread (s).", true)]
        public static readonly int[] Slots = { 3, 1, 1, 1, 1 };
        public static readonly float[] CommitCooldown = { 0.15f, 0.60f, 0.80f, 1.20f, 1.50f };
        public const int WindupCap = 4, WindupCapWhileGrabbed = 2;
        public const float SwipeSpreadSeconds = 0.12f;

        private static readonly int[] s_active = new int[AttackStyleProfile.StyleCount];
        private static readonly float[] s_lastCommit = { -99f, -99f, -99f, -99f, -99f };
        private static int s_windups;
        private static float s_lastSwipeWindup = -99f;

        public static int Active(ZombieAttackStyle s) => s_active[(int)s];
        public static int Windups => s_windups;
        /// <summary>Peak concurrent wind-ups since the last reset (tests / bot evidence).</summary>
        public static int PeakWindups { get; private set; }

        public static bool CanAcquire(ZombieAttackStyle s, float now)
        {
            int i = (int)s;
            if (s_active[i] >= Slots[i]) return false;
            if (now - s_lastCommit[i] < CommitCooldown[i]) return false;
            if (s_windups >= (GrabState.Active ? WindupCapWhileGrabbed : WindupCap)) return false;
            if (GrabState.Active && s == ZombieAttackStyle.Lunge) return false;          // no dive on a slowed, grabbed player
            if (s == ZombieAttackStyle.Swipe && now - s_lastSwipeWindup < SwipeSpreadSeconds) return false;
            return true;
        }

        public static void Acquire(ZombieAttackStyle s, float now)
        {
            s_active[(int)s]++;
            s_windups++;
            PeakWindups = Mathf.Max(PeakWindups, s_windups);
            if (s == ZombieAttackStyle.Swipe) s_lastSwipeWindup = now;
        }

        /// <summary>Wind-up → Active: the style's cooldown starts, the wind-up count drops.</summary>
        public static void Commit(ZombieAttackStyle s, float now)
        {
            s_lastCommit[(int)s] = now;
            s_windups = Mathf.Max(0, s_windups - 1);
        }

        /// <summary>The swing ended (recovery done, cancel, death); <paramref name="stillInWindup"/> when it never committed.</summary>
        public static void Release(ZombieAttackStyle s, bool stillInWindup)
        {
            s_active[(int)s] = Mathf.Max(0, s_active[(int)s] - 1);
            if (stillInWindup) s_windups = Mathf.Max(0, s_windups - 1);
        }

        public static void ResetStatics()
        {
            Array.Clear(s_active, 0, s_active.Length);
            for (int i = 0; i < s_lastCommit.Length; i++) s_lastCommit[i] = -99f;
            s_windups = 0; s_lastSwipeWindup = -99f; PeakWindups = 0;
        }
    }

    public enum GrabEndReason { Timer = 0, Escaped = 1, Killed = 2, Staggered = 3 }

    /// <summary>
    /// WP-076 §1.4 · the player can be held by at most one body. S-03 owns the rule; the player side (movement ×0.25,
    /// camera pull, HUD prompt) reads <see cref="Active"/> and the two events (S-02 / S-07 / S-09).
    /// </summary>
    public static class GrabState
    {
        [PrototypeOnly(PendingDecisions.None, "WP-076 §1.4 grab: player speed factor while held, escape impulse on the grabbing body, timer grab length by tier (clip grab_start→grab_end at rate 1.0 / 0.85), share of the bite paid at the hit / at grab_end.", true)]
        public const float PlayerSpeedFactor = 0.25f, EscapeImpulse = 0.85f, GrabSecondsStandard = 0.27f, GrabSecondsElite = 0.31f, GrabMaxSeconds = 0.6f,
                           BiteShareOnHit = 0.6f, BiteShareOnEnd = 0.4f;

        public static bool Active => ActiveLifeId != 0;
        public static int ActiveLifeId { get; private set; }
        public static EnemyTier ActiveTier { get; private set; }
        public static Transform ActiveBody { get; private set; }
        public static int StartedCount { get; private set; }
        public static int EscapedCount { get; private set; }
        public static GrabEndReason LastEndReason { get; private set; }

        public static event Action<Transform, EnemyTier, float> Started;   // body, tier, seconds
        public static event Action<Transform, GrabEndReason> Ended;

        public static bool TryBegin(int lifeId, Transform body, EnemyTier tier, float seconds)
        {
            if (Active || lifeId == 0) return false;
            ActiveLifeId = lifeId; ActiveTier = tier; ActiveBody = body; StartedCount++;
            try { Started?.Invoke(body, tier, seconds); } catch (Exception e) { Debug.LogException(e); }
            return true;
        }

        public static void End(int lifeId, GrabEndReason reason)
        {
            if (!Active || lifeId != ActiveLifeId) return;
            Transform body = ActiveBody;
            ActiveLifeId = 0; ActiveBody = null; LastEndReason = reason;
            if (reason == GrabEndReason.Escaped) EscapedCount++;
            try { Ended?.Invoke(body, reason); } catch (Exception e) { Debug.LogException(e); }
        }

        public static float SecondsFor(EnemyTier tier) => tier == EnemyTier.Elite ? GrabSecondsElite : GrabSecondsStandard;

        /// <summary>State and counters only — subscribers (S-02 slow, S-09 prompt) are kept (review R1-A #11).</summary>
        public static void ResetStatics() { ActiveLifeId = 0; ActiveBody = null; StartedCount = 0; EscapedCount = 0; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDomain() { ResetStatics(); Started = null; Ended = null; AttackSlotBoard.ResetStatics(); AttackSelector.Enabled = ReadFlag(); }

        /// <summary>Review R1-A #11: every run starts with an empty slot board and no grab, whatever the pool did.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BindRunReset()
        {
            DD.Prototype.Rounds.RunEvents.RunStarted -= OnRunStarted;
            DD.Prototype.Rounds.RunEvents.RunStarted += OnRunStarted;
        }

        private static void OnRunStarted(int startRound)
        {
            if (Active) End(ActiveLifeId, GrabEndReason.Killed);
            ActiveLifeId = 0; ActiveBody = null;
            AttackSlotBoard.ResetStatics();
        }

        /// <summary>`-ddAiV3 0` turns the v3 selector off (A/B, S-05 §5).</summary>
        private static bool ReadFlag()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-ddAiV3") return args[i + 1] != "0";
            return true;
        }
    }
}
