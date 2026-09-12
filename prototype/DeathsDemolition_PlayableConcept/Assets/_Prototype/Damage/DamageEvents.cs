using System;
using UnityEngine;
using DD.Prototype.Core.Config;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Damage
{
    // =====================================================================================
    // CROSS-TASK CONTRACT
    // -------------------------------------------------------------------------------------
    // G-07 (round / gold / kill counters) subscribes to EnemyDied.
    // G-09 (audio) subscribes to ArmorBroken, EnemyDamaged, EnemyDied, PlayerAttacked.
    // The feedback layer subscribes to everything and owns hit-stop and camera shake.
    //
    // Every payload is a readonly struct passed by value: subscribing costs one delegate,
    // raising costs zero allocations, which matters at 40 enemies under automatic fire.
    //
    // ORDERING GUARANTEE
    //   For a hit that both breaks a plate and kills: EnemyDamaged -> ArmorBroken -> EnemyDied.
    //   ArmorBroken fires once per piece, so one explosion can raise it three times.
    // =====================================================================================

    /// <summary>One armour piece went from protecting to broken. REQ-129.</summary>
    public readonly struct ArmorBreakInfo
    {
        /// <summary>Root transform of the enemy. Never null while the event is on the stack.</summary>
        public readonly Transform Owner;

        public readonly EnemyTier Tier;

        /// <summary>Which plate broke: Helmet, Body or Leg.</summary>
        public readonly ArmorSlot Slot;

        /// <summary>Region the plate was protecting.</summary>
        public readonly BodyRegion Region;

        /// <summary>World position to spawn shards / play the break sound at.</summary>
        public readonly Vector3 Point;

        /// <summary>Direction the killing blow travelled. Shards are thrown along it.</summary>
        public readonly Vector3 Direction;

        public readonly DamageSourceKind Source;

        /// <summary>Plates still protecting this enemy AFTER the break. 0..2.</summary>
        public readonly int RemainingProtectingPieces;

        /// <summary>
        /// Seconds of hit-stop this event is worth, straight from
        /// FeedbackConfigSection.HitStopArmorBreak. The feedback owner still applies its
        /// own MinInterval gate; this is a suggestion, not a command. REQ-127.
        /// </summary>
        public readonly float SuggestedHitStop;

        public ArmorBreakInfo(Transform owner, EnemyTier tier, ArmorSlot slot, BodyRegion region,
            Vector3 point, Vector3 direction, DamageSourceKind source, int remainingProtectingPieces,
            float suggestedHitStop)
        {
            Owner = owner;
            Tier = tier;
            Slot = slot;
            Region = region;
            Point = point;
            Direction = direction;
            Source = source;
            RemainingProtectingPieces = remainingProtectingPieces;
            SuggestedHitStop = suggestedHitStop;
        }
    }

    /// <summary>An enemy died. This is the gold / kill-counter / XP hook. Fires exactly once per enemy life.</summary>
    public readonly struct EnemyDeathInfo
    {
        /// <summary>Root transform of the enemy. Still alive as a GameObject (death animation is playing).</summary>
        public readonly Transform Owner;

        public readonly EnemyTier Tier;

        /// <summary>World position at the moment of death. Spawn the gold orb here.</summary>
        public readonly Vector3 Position;

        /// <summary>Travel direction of the killing blow. Death reactions and orbs use it.</summary>
        public readonly Vector3 KillDirection;

        /// <summary>Region the killing damage landed on.</summary>
        public readonly BodyRegion KillRegion;

        public readonly DamageSourceKind KillSource;

        /// <summary>Unarmoured head shot. Feedback plays the loud variant. See DamageRegions.IsCritical.</summary>
        public readonly bool WasCritical;

        /// <summary>Elite or Ultra. The top of the REQ-125 feedback hierarchy.</summary>
        public readonly bool WasHeavyDeath;

        /// <summary>Round this enemy was spawned into. G-07 uses it for per-round stats.</summary>
        public readonly int SpawnRound;

        /// <summary>
        /// Gold this kill is worth, already read from PrototypeConfig.GoldReward(tier).
        /// Provided so G-07 does not have to re-derive it; G-07 remains free to ignore it
        /// and read the config itself.
        /// </summary>
        public readonly int GoldReward;

        /// <summary>XP this kill is worth, from PrototypeConfig.KillXp(tier). Same rule as GoldReward.</summary>
        public readonly int KillXp;

        /// <summary>Damage past zero on the killing blow. Drives gib / heavy death selection.</summary>
        public readonly float Overkill;

        /// <summary>Suggested hit-stop seconds. Zero for ordinary Standard deaths. REQ-127.</summary>
        public readonly float SuggestedHitStop;

        /// <summary>
        /// Identity of the enemy LIFE that ended (WP-004): one <see cref="EnemyLifeIds"/>
        /// value per pool checkout, so a body recycled ten times has ten distinct ids.
        /// The gold reward is granted at most once per life; a producer that leaves this
        /// at 0 earns nothing.
        /// </summary>
        public readonly int LifeId;

        public EnemyDeathInfo(Transform owner, EnemyTier tier, Vector3 position, Vector3 killDirection,
            BodyRegion killRegion, DamageSourceKind killSource, bool wasCritical, bool wasHeavyDeath,
            int spawnRound, int goldReward, int killXp, float overkill, float suggestedHitStop)
            : this(owner, tier, position, killDirection, killRegion, killSource, wasCritical, wasHeavyDeath,
                spawnRound, goldReward, killXp, overkill, suggestedHitStop, 0)
        {
        }

        public EnemyDeathInfo(Transform owner, EnemyTier tier, Vector3 position, Vector3 killDirection,
            BodyRegion killRegion, DamageSourceKind killSource, bool wasCritical, bool wasHeavyDeath,
            int spawnRound, int goldReward, int killXp, float overkill, float suggestedHitStop, int lifeId)
        {
            Owner = owner;
            Tier = tier;
            Position = position;
            KillDirection = killDirection;
            KillRegion = killRegion;
            KillSource = killSource;
            WasCritical = wasCritical;
            WasHeavyDeath = wasHeavyDeath;
            SpawnRound = spawnRound;
            GoldReward = goldReward;
            KillXp = killXp;
            Overkill = overkill;
            SuggestedHitStop = suggestedHitStop;
            LifeId = lifeId;
        }
    }

    /// <summary>
    /// Issues one id per enemy life (pool checkout). Monotonic within a play session, so
    /// "did this life begin before the run opened" is a single comparison against the
    /// value read at run start. Reset with every other static on play-mode entry.
    /// </summary>
    public static class EnemyLifeIds
    {
        private static int s_last;

        /// <summary>The most recently issued id. 0 before any enemy has lived.</summary>
        public static int Last => s_last;

        /// <summary>A fresh id. Never 0, never repeats within a session.</summary>
        public static int Next()
        {
            return ++s_last;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_last = 0;
        }
    }

    /// <summary>Any non-lethal damage landed on an enemy. Fires for every bullet, so keep handlers cheap.</summary>
    public readonly struct EnemyDamageInfo
    {
        public readonly Transform Owner;
        public readonly EnemyTier Tier;
        public readonly DamageResult Result;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;
        public readonly DamageSourceKind Source;

        /// <summary>Impact force of the hit, in DamageForce units.</summary>
        public readonly float ImpactForce;

        /// <summary>Health fraction left after the hit, 0..1. Cheap health-bar feed.</summary>
        public readonly float HealthRatio;

        /// <summary>True when this hit produced a real stagger, not just a flinch. REQ-123.</summary>
        public readonly bool CausedStagger;

        public EnemyDamageInfo(Transform owner, EnemyTier tier, in DamageResult result, Vector3 point,
            Vector3 direction, DamageSourceKind source, float impactForce, float healthRatio, bool causedStagger)
        {
            Owner = owner;
            Tier = tier;
            Result = result;
            Point = point;
            Direction = direction;
            Source = source;
            ImpactForce = impactForce;
            HealthRatio = healthRatio;
            CausedStagger = causedStagger;
        }
    }

    /// <summary>
    /// An enemy attack's ACTIVE window actually overlapped the player - REQ-043 / DEC-029.
    /// Contact alone never raises this.
    /// </summary>
    public readonly struct EnemyAttackLandedInfo
    {
        public readonly Transform Attacker;
        public readonly EnemyTier Tier;

        /// <summary>Scaled damage, already through PrototypeConfig.EnemyAttackDamage(tier, round).</summary>
        public readonly float Damage;

        /// <summary>World position of the attack socket at the moment it connected.</summary>
        public readonly Vector3 Point;

        /// <summary>Attacker -> player, normalised. Drives the screen-edge indicator.</summary>
        public readonly Vector3 Direction;

        /// <summary>
        /// True when the enemy found an IDamageReceiver for the player and applied the damage
        /// itself. False means nobody was registered and the listener owns applying it.
        /// See EnemyAttackState for the fallback contract.
        /// </summary>
        public readonly bool DamageApplied;

        public EnemyAttackLandedInfo(Transform attacker, EnemyTier tier, float damage, Vector3 point,
            Vector3 direction, bool damageApplied)
        {
            Attacker = attacker;
            Tier = tier;
            Damage = damage;
            Point = point;
            Direction = direction;
            DamageApplied = damageApplied;
        }
    }

    /// <summary>
    /// Static, allocation-free gameplay event bus for combat.
    ///
    /// Subscribe in OnEnable, unsubscribe in OnDisable. Statics are cleared on every
    /// play-mode entry (SubsystemRegistration) so a domain-reload-disabled editor
    /// session cannot leak yesterday's handlers into today's run.
    ///
    /// A throwing subscriber is caught and logged so one bad listener cannot stop an
    /// enemy from dying.
    /// </summary>
    public static class DamageEvents
    {
        /// <summary>One armour plate broke. Raised once per plate, so an explosion can raise it 3x.</summary>
        public static event Action<ArmorBreakInfo> ArmorBroken;

        /// <summary>Damage landed on an enemy, lethal or not.</summary>
        public static event Action<EnemyDamageInfo> EnemyDamaged;

        /// <summary>An enemy died. G-07 gold and kill counters bind here.</summary>
        public static event Action<EnemyDeathInfo> EnemyDied;

        /// <summary>An enemy attack connected with the player.</summary>
        public static event Action<EnemyAttackLandedInfo> PlayerAttacked;

        public static void RaiseArmorBroken(in ArmorBreakInfo info)
        {
            Action<ArmorBreakInfo> handler = ArmorBroken;
            if (handler == null)
            {
                return;
            }

            try { handler.Invoke(info); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static void RaiseEnemyDamaged(in EnemyDamageInfo info)
        {
            Action<EnemyDamageInfo> handler = EnemyDamaged;
            if (handler == null)
            {
                return;
            }

            try { handler.Invoke(info); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static void RaiseEnemyDied(in EnemyDeathInfo info)
        {
            Action<EnemyDeathInfo> handler = EnemyDied;
            if (handler == null)
            {
                return;
            }

            try { handler.Invoke(info); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static void RaisePlayerAttacked(in EnemyAttackLandedInfo info)
        {
            Action<EnemyAttackLandedInfo> handler = PlayerAttacked;
            if (handler == null)
            {
                return;
            }

            try { handler.Invoke(info); }
            catch (Exception e) { Debug.LogException(e); }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ArmorBroken = null;
            EnemyDamaged = null;
            EnemyDied = null;
            PlayerAttacked = null;
        }
    }
}
