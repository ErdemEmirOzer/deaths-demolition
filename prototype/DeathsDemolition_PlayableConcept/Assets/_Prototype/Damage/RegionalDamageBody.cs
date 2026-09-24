using UnityEngine;
using DD.Prototype.Core.Config;
using BodyRegion = DD.Prototype.Damage.BodyRegion;

namespace DD.Prototype.Damage
{
    /// <summary>
    /// The regional armour + health model, as a plain C# object.
    ///
    /// This is the authoritative implementation of REQ-080..REQ-090. It owns no Unity
    /// state, spawns no effects and raises no events, which means it can be exercised
    /// by a headless test with a stub config. DD.Prototype.Enemies.ZombieEnemy wraps it,
    /// reads the outcome and turns it into visuals, audio and gameplay events.
    ///
    /// THE TWO RULES THAT ARE EASY TO GET WRONG
    ///   1. Surplus never carries. If a hit or a blast share exceeds the remaining
    ///      durability of a plate, the excess is destroyed, not forwarded to health.
    ///      (REQ-083 for direct hits, REQ-090 for explosions.)
    ///   2. One blast is one damage budget. The three region shares are normalised to
    ///      sum to exactly 1, so a fully armoured enemy and a naked enemy absorb the
    ///      same total number - it is only distributed differently. (REQ-085.)
    /// </summary>
    public sealed class RegionalDamageBody
    {
        private const int RegionCount = 3;

        // ------------------------------------------------------ explosion share fallback
        // Used when no ExplosionConfigSection is supplied (headless tests, a scene opened
        // without Boot). They mirror ExplosionConfigSection's own request values EXACTLY;
        // a silent disagreement here would move the blast model without moving the config.

        [PrototypeOnly(PendingDecisions.ExplosionShares,
            "Fallback when no ExplosionConfigSection is supplied. Request value: 25%.")]
        private const float FallbackHeadShare = 0.25f;

        [PrototypeOnly(PendingDecisions.ExplosionShares,
            "Fallback when no ExplosionConfigSection is supplied. Request value: 45%.")]
        private const float FallbackUpperBodyShare = 0.45f;

        [PrototypeOnly(PendingDecisions.ExplosionShares,
            "Fallback when no ExplosionConfigSection is supplied. Request value: 30%.")]
        private const float FallbackLowerBodyShare = 0.30f;

        private readonly ArmorSlot[] _breaks = new ArmorSlot[RegionCount];
        private readonly float[] _regionHealthDamage = new float[RegionCount];
        private readonly float[] _regionArmorDamage = new float[RegionCount];

        public ArmorSet Armor { get; } = new ArmorSet();

        public float MaxHealth { get; private set; }
        public float Health { get; private set; }
        public bool IsDead => Health <= 0f;
        public float HealthRatio => MaxHealth > 0f ? Mathf.Clamp01(Health / MaxHealth) : 0f;

        /// <summary>Damage past 0 health on the killing call. Feeds heavy-death feedback.</summary>
        public float LastOverkill { get; private set; }

        /// <summary>How many armour pieces broke during the most recent Apply call. 0..3.</summary>
        public int LastBreakCount { get; private set; }

        public ArmorSlot LastBreak(int index)
        {
            return index >= 0 && index < LastBreakCount ? _breaks[index] : ArmorSlot.None;
        }

        /// <summary>Per-region breakdown of the most recent call. Used by the debug readout and by FX placement.</summary>
        public float LastHealthDamageIn(BodyRegion region) => _regionHealthDamage[(int)region];

        public float LastArmorDamageIn(BodyRegion region) => _regionArmorDamage[(int)region];

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Full reset for a pooled respawn. Strips armour, refills health.</summary>
        public void Reset(float maxHealth)
        {
            MaxHealth = Mathf.Max(1f, maxHealth);
            Health = MaxHealth;
            LastOverkill = 0f;
            Armor.Clear();
            ClearLast();
        }

        /// <summary>
        /// Live config edit landed and the enemy's max health moved. Keeps the current
        /// damage ratio so an enemy at 40% health stays at 40% instead of healing.
        /// </summary>
        public void RescaleMaxHealth(float newMaxHealth)
        {
            newMaxHealth = Mathf.Max(1f, newMaxHealth);
            if (Mathf.Approximately(newMaxHealth, MaxHealth))
            {
                return;
            }

            float ratio = HealthRatio;
            MaxHealth = newMaxHealth;
            Health = IsDead ? 0f : Mathf.Max(0.01f, MaxHealth * ratio);
        }

        /// <summary>Debug / cheat kill. Returns a result shaped like a normal kill.</summary>
        public DamageResult Execute(BodyRegion region)
        {
            ClearLast();

            var result = new DamageResult { Region = region };
            if (IsDead)
            {
                return result;
            }

            result.HealthDamage = Health;
            _regionHealthDamage[(int)region] = Health;
            Health = 0f;
            LastOverkill = 0f;
            result.Killed = true;
            return result;
        }

        // ------------------------------------------------------------- direct hit path

        /// <summary>
        /// REQ-082, REQ-083, REQ-084.
        ///
        /// Armoured region -> the whole hit goes into that plate, clamped, surplus discarded,
        ///                    health untouched, ArmorBroke set if the plate died this call.
        /// Exposed region   -> health takes Amount * exposed multiplier
        ///                    (head 2x, upper 1x, lower 0.8x - all PROTOTYPE-ONLY, from config).
        /// </summary>
        public DamageResult ApplyDirectHit(in DamageHit hit, ArmorConfigSection armorConfig)
        {
            ClearLast();

            var result = new DamageResult { Region = hit.Region };

            if (IsDead || hit.Amount <= 0f)
            {
                return result;
            }

            ArmorSlot slot = DamageRegions.SlotFor(hit.Region);

            if (Armor.Protects(slot))
            {
                // ---- protected: nothing reaches health, no matter how big the hit is.
                // WP-069: the weapon's armour multiplier scales what the plate takes (sr_1 ×1.6 cracks, smg_1 ×0.7 scratches).
                float armorAmount = hit.Amount * DamageHitContext.ArmorMultiplier;
                Armor.Absorb(slot, armorAmount, out float absorbed, out bool broke);

                result.WasArmored = true;
                result.ArmorDamage = absorbed;
                _regionArmorDamage[(int)hit.Region] = absorbed;

                if (broke)
                {
                    result.ArmorBroke = true;
                    _breaks[LastBreakCount++] = slot;
                }

                return result;
            }

            // ---- exposed: straight to health, with the regional multiplier.
            float multiplier = armorConfig != null
                ? armorConfig.ExposedMultiplierFor(DamageRegions.ToCore(hit.Region))
                : 1f;

            // WP-069: a weapon's extra head multiplier rides on top of the regional ×2.0 (ar_2 ×1.15, sr_1 ×1.25).
            if (hit.Region == BodyRegion.Head)
            {
                multiplier *= DamageHitContext.HeadMultiplier;
            }

            float healthDamage = hit.Amount * multiplier;
            ApplyHealthDamage(healthDamage, hit.Region, ref result);
            return result;
        }

        // -------------------------------------------------------------- explosion path

        /// <summary>
        /// REQ-085..REQ-090.
        ///
        /// Splits one blast budget across the three regions and settles each region
        /// independently. Consequences that fall out of this, all required:
        ///   * Fully armoured enemy takes zero health damage until a plate breaks (REQ-088)
        ///     - every share lands on a plate, and surplus never carries.
        ///   * Partially armoured enemy takes exactly the sum of its EXPOSED shares (REQ-087).
        ///   * No regional critical multiplier is applied anywhere here (REQ-089).
        ///   * Adding armour never increases the total absorbed (REQ-085) because the
        ///     shares are normalised before use.
        /// </summary>
        public DamageResult ApplyExplosion(
            float totalDamage,
            ArmorConfigSection armorConfig,
            ExplosionConfigSection explosionConfig)
        {
            ClearLast();

            var result = new DamageResult { Region = BodyRegion.UpperBody };

            if (IsDead || totalDamage <= 0f)
            {
                return result;
            }

            // Normalise so a live config edit can never inflate or deflate the blast budget.
            float head = explosionConfig != null ? Mathf.Max(0f, explosionConfig.HeadShare) : FallbackHeadShare;
            float upper = explosionConfig != null ? Mathf.Max(0f, explosionConfig.UpperBodyShare) : FallbackUpperBodyShare;
            float lower = explosionConfig != null ? Mathf.Max(0f, explosionConfig.LowerBodyShare) : FallbackLowerBodyShare;

            float sum = head + upper + lower;
            if (sum <= 0.0001f)
            {
                head = FallbackHeadShare;
                upper = FallbackUpperBodyShare;
                lower = FallbackLowerBodyShare;
                sum = 1f;
            }

            float inv = 1f / sum;
            SettleExplosionShare(BodyRegion.Head, totalDamage * head * inv, ref result);
            SettleExplosionShare(BodyRegion.UpperBody, totalDamage * upper * inv, ref result);
            SettleExplosionShare(BodyRegion.LowerBody, totalDamage * lower * inv, ref result);

            // Pick the region the feedback layer should point at.
            result.Region = ResolveExplosionReportRegion(result);
            result.WasArmored = result.HealthDamage <= 0f && result.ArmorDamage > 0f;

            if (result.HealthDamage > 0f)
            {
                CommitHealth(ref result);
            }

            return result;
        }

        private void SettleExplosionShare(BodyRegion region, float share, ref DamageResult result)
        {
            if (share <= 0f)
            {
                return;
            }

            ArmorSlot slot = DamageRegions.SlotFor(region);

            if (Armor.Protects(slot))
            {
                Armor.Absorb(slot, share, out float absorbed, out bool broke);
                result.ArmorDamage += absorbed;
                _regionArmorDamage[(int)region] = absorbed;

                if (broke)
                {
                    result.ArmorBroke = true;
                    _breaks[LastBreakCount++] = slot;
                }

                // REQ-090: share - absorbed is discarded here. It does not reach health.
                return;
            }

            // REQ-089: no head/upper/lower critical multiplier on blast health damage.
            result.HealthDamage += share;
            _regionHealthDamage[(int)region] = share;
        }

        private BodyRegion ResolveExplosionReportRegion(in DamageResult result)
        {
            if (result.HealthDamage > 0f)
            {
                BodyRegion best = BodyRegion.UpperBody;
                float bestValue = -1f;
                for (int i = 0; i < RegionCount; i++)
                {
                    if (_regionHealthDamage[i] > bestValue)
                    {
                        bestValue = _regionHealthDamage[i];
                        best = (BodyRegion)i;
                    }
                }

                return best;
            }

            if (LastBreakCount > 0)
            {
                return DamageRegions.RegionFor(_breaks[0]);
            }

            BodyRegion loudest = BodyRegion.UpperBody;
            float loudestValue = -1f;
            for (int i = 0; i < RegionCount; i++)
            {
                if (_regionArmorDamage[i] > loudestValue)
                {
                    loudestValue = _regionArmorDamage[i];
                    loudest = (BodyRegion)i;
                }
            }

            return loudest;
        }

        // ------------------------------------------------------------------- internals

        private void ApplyHealthDamage(float amount, BodyRegion region, ref DamageResult result)
        {
            if (amount <= 0f)
            {
                return;
            }

            result.HealthDamage = amount;
            _regionHealthDamage[(int)region] = amount;
            CommitHealth(ref result);
        }

        private void CommitHealth(ref DamageResult result)
        {
            float before = Health;
            Health -= result.HealthDamage;

            if (Health <= 0f)
            {
                Health = 0f;
                LastOverkill = result.HealthDamage - before;
                result.Killed = true;
            }
        }

        private void ClearLast()
        {
            LastBreakCount = 0;
            for (int i = 0; i < RegionCount; i++)
            {
                _regionHealthDamage[i] = 0f;
                _regionArmorDamage[i] = 0f;
            }
        }
    }
}
