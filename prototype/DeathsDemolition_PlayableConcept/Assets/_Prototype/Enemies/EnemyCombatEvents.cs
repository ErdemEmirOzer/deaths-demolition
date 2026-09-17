using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Enemies
{
    /// <summary>The enemy combat beats (WP-040, ADR-040-1). Names = the coverage-matrix rows.</summary>
    public enum EnemyCombatEventKind
    {
        /// <summary>telegraph_start · the wind-up began (DEC-029 telegraph; the clip's anticipation).</summary>
        TelegraphStart = 0,
        /// <summary>attack_commit · the hit window opened (clip hit_window_start, or the timer on a placeholder body).</summary>
        AttackCommit = 1,
        /// <summary>attack_hit · the strike connected with the player (the ONLY damage frame of the swing).</summary>
        AttackHit = 2,
        /// <summary>attack_miss · the hit window closed without a connection (whiff; the recovery / punish window follows).</summary>
        AttackMiss = 3,
        /// <summary>stagger · a gameplay stagger began (REQ-124 window) or the full-body heavy stagger (armour break).</summary>
        Stagger = 4,
        /// <summary>armor_break_&lt;region&gt; · a plate group broke (Region = the slot).</summary>
        ArmorBreak = 5,
        /// <summary>death_&lt;tier&gt; · the body died (Tier = which death row).</summary>
        Death = 6,
        /// <summary>spawn_rise · the body started rising from the ground (DEC-081: off-screen spawns only).</summary>
        SpawnRise = 7,
        /// <summary>spawn_land · the body is up and walking (rise_end, or immediately on a spawn without a rise).</summary>
        SpawnLand = 8,
    }

    /// <summary>
    /// One combat beat. Readonly struct by value (no allocation per event); the body transform
    /// is the enemy root, Position / Direction are world space at the moment of the beat
    /// (the attack socket for swings, the hit point for breaks, the feet for spawn beats).
    /// </summary>
    public readonly struct EnemyCombatEventInfo
    {
        public EnemyCombatEventInfo(EnemyCombatEventKind kind, Transform body, EnemyTier tier, int lifeId,
            Vector3 position, Vector3 direction, ArmorSlot region, bool animationDriven, float seconds,
            ZombieAttackStyle style = ZombieAttackStyle.Swipe)
        {
            Style = style;
            Kind = kind;
            Body = body;
            Tier = tier;
            LifeId = lifeId;
            Position = position;
            Direction = direction;
            Region = region;
            AnimationDriven = animationDriven;
            Seconds = seconds;
        }

        public EnemyCombatEventKind Kind { get; }
        /// <summary>The enemy root (pooled; do not cache across lives - use <see cref="LifeId"/>).</summary>
        public Transform Body { get; }
        public EnemyTier Tier { get; }
        /// <summary>The life this beat belongs to (EnemyLifeIds); a recycled body starts a new one.</summary>
        public int LifeId { get; }
        public Vector3 Position { get; }
        /// <summary>Facing / strike direction (unit, flat) — the swing's forward, the break's hit direction.</summary>
        public Vector3 Direction { get; }
        /// <summary>Only for <see cref="EnemyCombatEventKind.ArmorBreak"/>; <see cref="ArmorSlot.None"/> otherwise.</summary>
        public ArmorSlot Region { get; }
        /// <summary>True when the beat came from an animation event (AP-009 clip); false = timer fallback (placeholder / block body).</summary>
        public bool AnimationDriven { get; }
        /// <summary>The beat's own duration when it has one (telegraph / rise seconds), else 0.</summary>
        public float Seconds { get; }
        /// <summary>WP-052: the swing's style for the attack beats (Lunge / Swipe / Sweep / Overhead); Swipe otherwise.</summary>
        public ZombieAttackStyle Style { get; }

        /// <summary>The coverage-matrix row name: kind, with the region / tier suffix where the matrix has one.</summary>
        public string RowName => EnemyCombatEvents.RowName(Kind, Region, Tier);
    }

    /// <summary>
    /// WP-040 · ADR-040-1 · THE ONE SOURCE of enemy combat beats. Every consumer (S-07 VFX,
    /// S-08 audio, HUD threat chips, tests) subscribes to <see cref="Raised"/>; nothing else
    /// republishes these beats. The raise sites are all in <c>ZombieEnemy</c>:
    ///
    ///   telegraph_start  attack Begin (phase None → Windup)
    ///   attack_commit    phase Windup → Active (clip hit_window_start; timer fallback on a placeholder)
    ///   attack_hit       the connect frame (same frame as DamageEvents.PlayerAttacked)
    ///   attack_miss      phase Active → Recovery with no connection this swing
    ///   stagger          BeginStagger (REQ-124) / the heavy stagger clip on an armour break
    ///   armor_break_*    same site as DamageEvents.ArmorBroken (one per broken slot)
    ///   death_*          Die, once per life (same frame as DamageEvents.EnemyDied)
    ///   spawn_rise       Initialise, when the rise plays (DEC-081 policy)
    ///   spawn_land       rise_end (clip) / the rise timer, or Initialise itself without a rise
    ///
    /// The existing WP-002 DamageEvents stay as they are (damage numbers, kill attribution);
    /// this surface is the BEAT layer on top: phase edges that DamageEvents never carried
    /// (telegraph, commit, miss, rise/land) plus the same break / death moments so a consumer
    /// needs one subscription. An exception in a handler is contained at the raise site.
    /// </summary>
    public static class EnemyCombatEvents
    {
        public const int KindCount = 9;

        /// <summary>The matrix row roots, index = (int)EnemyCombatEventKind.</summary>
        public static readonly string[] RowRoots =
        {
            "telegraph_start", "attack_commit", "attack_hit", "attack_miss", "stagger",
            "armor_break", "death", "spawn_rise", "spawn_land"
        };

        public static event Action<EnemyCombatEventInfo> Raised;

        private static readonly int[] s_counts = new int[KindCount];
        private static int s_total;
        private static EnemyCombatEventInfo s_last;

        /// <summary>Beats raised since the last reset, by kind (tests / probes).</summary>
        public static int Count(EnemyCombatEventKind kind) => s_counts[(int)kind];
        public static int Total => s_total;
        public static EnemyCombatEventInfo Last => s_last;

        public static void ResetStatics()
        {
            Array.Clear(s_counts, 0, s_counts.Length);
            s_total = 0;
            s_last = default;
        }

        public static string RowName(EnemyCombatEventKind kind, ArmorSlot region, EnemyTier tier)
        {
            switch (kind)
            {
                case EnemyCombatEventKind.ArmorBreak: return RowRoots[(int)kind] + "_" + RegionSuffix(region);
                case EnemyCombatEventKind.Death: return RowRoots[(int)kind] + "_" + tier.ToString().ToLowerInvariant();
                default: return RowRoots[(int)kind];
            }
        }

        private static string RegionSuffix(ArmorSlot slot)
        {
            switch (slot)
            {
                case ArmorSlot.Helmet: return "head";
                case ArmorSlot.Body: return "torso";
                case ArmorSlot.Leg: return "leg";
                default: return "none";
            }
        }

        public static void Raise(in EnemyCombatEventInfo info)
        {
            s_counts[(int)info.Kind]++;
            s_total++;
            s_last = info;

            Action<EnemyCombatEventInfo> handlers = Raised;
            if (handlers == null)
            {
                return;
            }

            // Same isolation as DamageEvents (one invoke, contained): the beat layer runs on the
            // enemy tick and the hit path, so no per-raise allocation (no GetInvocationList).
            try { handlers.Invoke(info); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
