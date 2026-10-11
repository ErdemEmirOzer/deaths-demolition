using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// WP-104 B3 · the body side of the run tree's statuses as plain arithmetic (EditMode-testable, no state, no allocation).
    /// The numbers come from <see cref="DD.Prototype.Rounds.TreeStats"/> (S-04 resolves them from run-tree-v2.json); the tier
    /// rules are GD-TREE §2 (el_ice2, kx_nuke, kx_hole, kx_cap, sv_bulwark) as agreed with S-04 on 12.10:
    ///   freeze      Standard / Special the full length, Elite × FreezeEliteShare, the Ultra never freezes (slows instead)
    ///   stun        Elite / Ultra × StunHeavyShare
    ///   pull        Standard 1, Special × HolePullSpecialShare, Elite / Ultra × HolePullHeavyShare
    /// A share ≤ 0 means "this tier is immune", never "use a default".
    /// </summary>
    public static class TreeStatusMath
    {
        [PrototypeOnly(PendingDecisions.None, "WP-104 B3: floor of the ice / freeze-slow walking factor — a guard only (the design's worst is 4 × 10 % = 0.6, the Ultra's 0.5).", true)]
        public const float MinSpeedFactor = 0.1f;

        [PrototypeOnly(PendingDecisions.None, "WP-104 B3: the pull stops this far outside the body radius from the hole's centre (m), so a pulled crowd rings the core instead of fighting the separation solver over one point.", true)]
        public const float PullCoreMetres = 0.6f;

        [PrototypeOnly(PendingDecisions.None, "WP-104 B3 (S-04 S03-RECEIVER §5.2): past the core the pull ramps up linearly over this many metres, so a pulled crowd settles on the ring instead of slamming into it.", true)]
        public const float PullTaperMetres = 1f;

        [PrototypeOnly(PendingDecisions.None, "WP-104 B3 (S-04 S03-RECEIVER §5.1): share of the crowd correction a pulled body takes — separation still works, but no longer fights the well at full strength (MaxCorrectionSpeed 2.8 < pull).", true)]
        public const float PulledCrowdShare = 0.5f;

        [PrototypeOnly(PendingDecisions.None, "WP-104 B3: a status tint change smaller than this (0..1) raises no StatusTintChanged (S-07 binds per change, never per tick).", true)]
        public const float TintEventStep = 0.05f;

        /// <summary>Current stacks plus <paramref name="add"/>, clamped to [0, <paramref name="max"/>]; 0 when the cap is off.</summary>
        public static int AddStacks(int current, int add, int max)
        {
            if (max <= 0) return 0;
            int n = (current > 0 ? current : 0) + (add > 0 ? add : 0);
            return n > max ? max : n;
        }

        /// <summary>The walking factor of <paramref name="stacks"/> ice stacks: exactly 1 with none.</summary>
        public static float IceSpeedFactor(int stacks, float slowPerStack)
        {
            if (stacks <= 0 || slowPerStack <= 0f) return 1f;
            return Mathf.Max(MinSpeedFactor, 1f - stacks * slowPerStack);
        }

        /// <summary>The Ultra's walking factor instead of a freeze (FreezeUltraSlow is the share it slows BY).</summary>
        public static float UltraSlowFactor(float ultraSlowShare) => Mathf.Clamp(1f - ultraSlowShare, MinSpeedFactor, 1f);

        /// <summary>Share of the freeze length this tier takes (the Ultra 0: it never freezes).</summary>
        public static float FreezeShare(EnemyTier tier, float eliteShare)
        {
            switch (tier)
            {
                case EnemyTier.Ultra: return 0f;
                case EnemyTier.Elite: return Mathf.Clamp01(eliteShare);
                default: return 1f;
            }
        }

        /// <summary>Share of a stun's length this tier takes.</summary>
        public static float StunShare(EnemyTier tier, float heavyShare) =>
            tier == EnemyTier.Elite || tier == EnemyTier.Ultra ? Mathf.Clamp01(heavyShare) : 1f;

        /// <summary>Share of the hole's pull speed this tier feels.</summary>
        public static float PullShare(EnemyTier tier, float specialShare, float heavyShare)
        {
            switch (tier)
            {
                case EnemyTier.Standard: return 1f;
                case EnemyTier.Special: return Mathf.Clamp01(specialShare);
                default: return Mathf.Clamp01(heavyShare);
            }
        }

        /// <summary>A hold that extends, never stacks: max(until, now + seconds).</summary>
        public static float Extend(float until, float now, float seconds) => seconds <= 0f ? until : Mathf.Max(until, now + seconds);

        /// <summary>When the next freeze may start: the cooldown counts from the end of this freeze (or Ultra slow).</summary>
        public static float NextFreezeReady(float holdEnd, float cooldown) => holdEnd + Mathf.Max(0f, cooldown);

        /// <summary>
        /// The cap may freeze now: the body's FreezeCooldown has run out, or the caller skips it (<paramref name="ignoreCooldown"/>:
        /// only S-04's Frozen Gravity synergy, §4.4 approved 12.10). The tier rules apply either way.
        /// </summary>
        public static bool FreezeReady(float now, float readyAt, bool ignoreCooldown) => ignoreCooldown || now >= readyAt;

        /// <summary>
        /// The pull velocity (planar) toward the centre for this frame: <paramref name="speed"/> along the line, ramped up over
        /// <see cref="PullTaperMetres"/> past the core, never past the core (≤ the remaining distance / dt), zero inside it.
        /// </summary>
        public static Vector3 PullVelocity(Vector3 toCentre, float speed, float coreRadius, float dt)
        {
            float distance = Mathf.Sqrt(toCentre.x * toCentre.x + toCentre.z * toCentre.z);
            if (speed <= 0f || distance <= coreRadius || distance < 1e-4f) return Vector3.zero;
            float v = speed * Mathf.Clamp01((distance - coreRadius) / PullTaperMetres);
            if (dt > 0f) v = Mathf.Min(v, (distance - coreRadius) / dt);
            float k = v / distance;
            return new Vector3(toCentre.x * k, 0f, toCentre.z * k);
        }
    }
}
