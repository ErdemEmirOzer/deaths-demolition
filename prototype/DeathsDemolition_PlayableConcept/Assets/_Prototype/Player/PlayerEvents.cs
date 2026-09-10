using UnityEngine;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// One hit against the player, as the attacker describes it.
    ///
    /// Attackers (enemy attack hitboxes, explosions, anything else) fill this in and
    /// hand it to <see cref="PlayerHealth.ApplyDamage(in PlayerDamageRequest)"/>.
    /// Every field except <see cref="Amount"/> is optional: a request with only an
    /// amount is valid and produces a non-directional hit.
    ///
    /// There is no attacker-side cooldown and no per-source throttle anywhere in this
    /// struct on purpose. REQ-044: the player gets no invulnerability window, and two
    /// attacks landing on the same frame both apply their full damage.
    /// </summary>
    public readonly struct PlayerDamageRequest
    {
        /// <summary>Damage before any clamping. Must be positive to have an effect.</summary>
        public readonly float Amount;

        /// <summary>Where the hit landed, for effects. Vector3.zero means unspecified.</summary>
        public readonly Vector3 WorldPoint;

        /// <summary>Where the attack came from, used to derive the hit direction.</summary>
        public readonly Vector3 SourcePosition;

        /// <summary>
        /// Free-form attacker tag, e.g. "Zombie.Standard.Swipe". Used for telemetry,
        /// the death screen and audio selection. Never parsed for gameplay logic.
        /// </summary>
        public readonly string SourceId;

        /// <summary>Optional attacker object, for effects that want to point back at it.</summary>
        public readonly Object Source;

        public PlayerDamageRequest(
            float amount,
            Vector3 worldPoint = default,
            Vector3 sourcePosition = default,
            string sourceId = null,
            Object source = null)
        {
            Amount = amount;
            WorldPoint = worldPoint;
            SourcePosition = sourcePosition;
            SourceId = sourceId;
            Source = source;
        }
    }

    /// <summary>
    /// What actually happened to the player. Raised after the health value has already
    /// been written, so a listener reading <see cref="PlayerHealth.Current"/> sees the
    /// same number as <see cref="HealthAfter"/>.
    /// </summary>
    public readonly struct PlayerDamageInfo
    {
        public readonly PlayerHealth Player;

        /// <summary>What the attacker asked for.</summary>
        public readonly float RequestedAmount;

        /// <summary>What was actually removed, after clamping at zero health.</summary>
        public readonly float AppliedAmount;

        public readonly float HealthBefore;
        public readonly float HealthAfter;
        public readonly float MaxHealth;

        /// <summary>Impact point in world space, or the player's position when unspecified.</summary>
        public readonly Vector3 WorldPoint;

        /// <summary>
        /// Normalised, horizontal, pointing FROM the attacker TOWARDS the player, i.e.
        /// the direction the blow travelled. Vector3.zero when the attacker gave no
        /// position.
        /// </summary>
        public readonly Vector3 HitDirection;

        public readonly string SourceId;

        /// <summary>True when this hit is the one that brought health to zero.</summary>
        public readonly bool WasFatal;

        public PlayerDamageInfo(
            PlayerHealth player,
            float requestedAmount,
            float appliedAmount,
            float healthBefore,
            float healthAfter,
            float maxHealth,
            Vector3 worldPoint,
            Vector3 hitDirection,
            string sourceId,
            bool wasFatal)
        {
            Player = player;
            RequestedAmount = requestedAmount;
            AppliedAmount = appliedAmount;
            HealthBefore = healthBefore;
            HealthAfter = healthAfter;
            MaxHealth = maxHealth;
            WorldPoint = worldPoint;
            HitDirection = hitDirection;
            SourceId = sourceId;
            WasFatal = wasFatal;
        }

        /// <summary>Health after the hit, as a 0..1 fraction of maximum.</summary>
        public float NormalizedAfter
        {
            get { return MaxHealth > 0f ? Mathf.Clamp01(HealthAfter / MaxHealth) : 0f; }
        }
    }

    /// <summary>
    /// The full health picture, raised on every change including regeneration ticks.
    /// This is what the HUD binds to; it never has to poll.
    /// </summary>
    public readonly struct PlayerHealthSnapshot
    {
        public readonly PlayerHealth Player;
        public readonly float Current;
        public readonly float Max;

        /// <summary>True while the regeneration delay has elapsed and health is climbing.</summary>
        public readonly bool IsRegenerating;

        /// <summary>Seconds left before regeneration starts. 0 while already regenerating.</summary>
        public readonly float SecondsUntilRegen;

        public readonly bool IsDead;

        public PlayerHealthSnapshot(
            PlayerHealth player, float current, float max, bool isRegenerating, float secondsUntilRegen, bool isDead)
        {
            Player = player;
            Current = current;
            Max = max;
            IsRegenerating = isRegenerating;
            SecondsUntilRegen = secondsUntilRegen;
            IsDead = isDead;
        }

        public float Normalized
        {
            get { return Max > 0f ? Mathf.Clamp01(Current / Max) : 0f; }
        }
    }

    /// <summary>
    /// The run is over. There is no revive and no second life (REQ-048), so this fires
    /// exactly once per run.
    /// </summary>
    public readonly struct PlayerDeathInfo
    {
        public readonly PlayerHealth Player;

        /// <summary>Where the player fell.</summary>
        public readonly Vector3 Position;

        /// <summary>Direction the killing blow travelled, or Vector3.zero.</summary>
        public readonly Vector3 KillingHitDirection;

        /// <summary>Damage of the killing blow that exceeded the remaining health.</summary>
        public readonly float Overkill;

        /// <summary>SourceId of the killing blow, may be null.</summary>
        public readonly string KilledBy;

        /// <summary>Time.time when it happened.</summary>
        public readonly float TimeOfDeath;

        public PlayerDeathInfo(
            PlayerHealth player,
            Vector3 position,
            Vector3 killingHitDirection,
            float overkill,
            string killedBy,
            float timeOfDeath)
        {
            Player = player;
            Position = position;
            KillingHitDirection = killingHitDirection;
            Overkill = overkill;
            KilledBy = killedBy;
            TimeOfDeath = timeOfDeath;
        }
    }
}
