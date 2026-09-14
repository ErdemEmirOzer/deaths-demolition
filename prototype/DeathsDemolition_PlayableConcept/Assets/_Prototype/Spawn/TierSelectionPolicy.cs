using System.Collections.Generic;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// WP-010 (ADR-010-1): the tier selection's only source of chance, so a test or the
    /// development probe can inject a seeded sequence and prove "same seed + same inputs =
    /// same round composition". The shipped implementation delegates to
    /// <see cref="Random.value"/>, which keeps the production distribution exactly what the
    /// WP-003 selector drew.
    /// </summary>
    public interface IEnemyTierRandom
    {
        /// <summary>Uniform 0..1, as <see cref="Random.value"/>.</summary>
        float Next01();
    }

    /// <summary>The shipped source: <see cref="Random.value"/>, untouched. Stateless, one shared instance.</summary>
    public sealed class UnityTierRandomSource : IEnemyTierRandom
    {
        public static readonly UnityTierRandomSource Instance = new UnityTierRandomSource();

        private UnityTierRandomSource()
        {
        }

        public float Next01() => Random.value;
    }

    /// <summary>
    /// Deterministic source for tests and the development probe (xorshift32, the same
    /// generator the enemies use for their own seeds). The seed is mixed (splitmix32
    /// finaliser) before use, so a small seed such as 1 or 21 does not start the stream
    /// with a near-zero first draw - a test that passed only because of that artefact
    /// would be lying (PRA K-3). Not used by production code.
    /// </summary>
    public sealed class SeededTierRandom : IEnemyTierRandom
    {
        private uint _state;

        /// <summary>How many rolls were consumed. Test readout.</summary>
        public int Calls { get; private set; }

        public SeededTierRandom(uint seed)
        {
            uint z = seed + 0x9E3779B9u;
            z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
            z = (z ^ (z >> 13)) * 0xC2B2AE35u;
            z ^= z >> 16;
            _state = z == 0u ? 0x9E3779B9u : z;
        }

        public float Next01()
        {
            Calls++;
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return (x >> 8) * (1f / 16777216f);
        }
    }

    /// <summary>One answer of the policy: which tier, and whether it was owed rather than drawn.</summary>
    public readonly struct TierPick
    {
        public TierPick(EnemyTier tier, bool guaranteed, bool forced)
        {
            Tier = tier;
            Guaranteed = guaranteed;
            Forced = forced;
        }

        public EnemyTier Tier { get; }

        /// <summary>True when this spawn discharges the tier's REQ-071 debut for the round.</summary>
        public bool Guaranteed { get; }

        /// <summary>True when this spawn discharges a DEC-021 forced count (an authored minimum), not the debut.</summary>
        public bool Forced { get; }
    }

    /// <summary>
    /// THE tier authority of a run (WP-010, ADR-010-1/2/3). Pure: no Unity object, no
    /// scene, no static state; the caller owns one instance per director and feeds it
    /// the round's pool numbers. Everything the WP-003 selector and the WP-000 scheduler
    /// each claimed is here once:
    ///
    ///   1. REQ-071 guaranteed introduction. A tier whose <c>IntroductionRound</c> equals
    ///      this round is OWED at least once. The debut is released once
    ///      <c>releaseFraction</c> of the pool has been spent (pacing - it is not the
    ///      round's first enemy) or as soon as the pool is running out (remaining ≤ owed):
    ///      the guarantee outranks the pacing. Several debuts in one round are owed in
    ///      the config's tier-list order, which is deterministic.
    ///   2. DEC-021 forced counts. An authored override row may demand a minimum number
    ///      of a tier this round. Those are owed the same way, after the debuts; a debut
    ///      counts as one of its tier's forced minimum. Obligations never exceed the pool:
    ///      what does not fit is dropped LOUDLY (<see cref="ShortfallCount"/>, one warning
    ///      per round), never silently.
    ///   3. Weighted pool. Everything else is a weighted draw over the tiers whose
    ///      introduction round has passed, through <see cref="PrototypeConfig.TierSpawnWeight"/>
    ///      (override row first, then the formula). A tier is NEVER drawn before its
    ///      introduction round, whatever an override says - that is a configuration error
    ///      (<see cref="RoundDifficultyOverrideRules"/>), not a spawn.
    ///
    /// Allocation-free after construction: fixed arrays sized by the tier count.
    /// </summary>
    public sealed class TierSelectionPolicy
    {
        /// <summary>Upper bound of distinct tiers (the enum has four).</summary>
        public const int MaxTiers = 4;

        /// <summary>Hard cap on owed spawns per round (debuts + forced counts only - never the pool itself, which may be larger: pool(26) = 263). Far above any obligation count a shipped or authored table carries (debuts ≤ 3 per round); a table asking for more is clamped and reported through <see cref="ShortfallCount"/>, never silently dropped.</summary>
        public const int MaxObligations = 256;

        private readonly EnemyTier[] _owedTier = new EnemyTier[MaxObligations];
        private readonly bool[] _owedIsDebut = new bool[MaxObligations];
        private int _owedCount;
        private int _owedCursor;

        private readonly EnemyTier[] _candidates = new EnemyTier[MaxTiers];
        private readonly float[] _weights = new float[MaxTiers];

        private int _round;
        private int _poolTotal;
        private float _releaseAfterSpawns;
        private int _picks;

        /// <summary>Round this policy is serving (0 before the first BeginRound).</summary>
        public int Round => _round;

        /// <summary>Pool size the round was begun with.</summary>
        public int PoolTotal => _poolTotal;

        /// <summary>How many picks were handed out this round.</summary>
        public int Picks => _picks;

        /// <summary>Obligations (debuts + forced counts) still owed this round.</summary>
        public int PendingObligations => _owedCount - _owedCursor;

        /// <summary>Debuts still owed this round (REQ-071).</summary>
        public int PendingGuaranteed
        {
            get
            {
                int n = 0;
                for (int i = _owedCursor; i < _owedCount; i++)
                {
                    if (_owedIsDebut[i]) n++;
                }

                return n;
            }
        }

        /// <summary>Obligations the round's pool could not hold, cumulative over the rounds of this instance (should stay 0; a non-zero value is reported, never hidden). <see cref="Clear"/> keeps it: a readout counter, not round state.</summary>
        public int ShortfallCount { get; private set; }

        /// <summary>Round of the last shortfall, 0 when none.</summary>
        public int LastShortfallRound { get; private set; }

        /// <summary>Forced-count rows that named a tier before its introduction round (configuration error, ignored at runtime).</summary>
        public int IgnoredForcedCounts { get; private set; }

        /// <summary>
        /// Start a round. <paramref name="poolTotal"/> is the round's whole pool
        /// (<c>RoundPlan.PoolTotal</c>); <paramref name="releaseFraction"/> is the share of it
        /// that must be spent before an owed debut is released.
        /// </summary>
        public void BeginRound(int round, PrototypeConfig config, int poolTotal, float releaseFraction)
        {
            _round = Mathf.Max(1, round);
            _poolTotal = Mathf.Max(1, poolTotal);
            _releaseAfterSpawns = _poolTotal * Mathf.Clamp01(releaseFraction);
            _picks = 0;
            _owedCount = 0;
            _owedCursor = 0;

            if (config == null)
            {
                return;
            }

            // 1. Debuts, in the config's tier-list order (deterministic across runs).
            List<EnemyTierSettings> tiers = config.Enemies.Tiers;
            for (int i = 0; i < tiers.Count && _owedCount < _owedTier.Length; i++)
            {
                EnemyTierSettings t = tiers[i];
                if (t == null || t.Tier == EnemyTier.Standard || t.IntroductionRound != _round)
                {
                    continue;
                }

                if (IndexOfOwed(t.Tier) >= 0)
                {
                    continue;   // one debut per tier even if the list carries a duplicate entry
                }

                _owedTier[_owedCount] = t.Tier;
                _owedIsDebut[_owedCount] = true;
                _owedCount++;
            }

            // 2. Forced counts (DEC-021). The debut already counts as one of the tier's minimum.
            int overflow = 0;
            if (config.TryGetRoundOverride(_round, out RoundDifficultyOverride ovr) && ovr.OverrideForcedCounts && ovr.ForcedTierCounts != null)
            {
                for (int i = 0; i < ovr.ForcedTierCounts.Count; i++)
                {
                    TierCountOverride row = ovr.ForcedTierCounts[i];
                    if (row == null || row.Count <= 0)
                    {
                        continue;
                    }

                    if (!IsIntroduced(config, row.Tier, _round))
                    {
                        IgnoredForcedCounts++;
                        continue;
                    }

                    int already = CountOwed(row.Tier);
                    int missing = row.Count - already;
                    for (int k = 0; k < missing; k++)
                    {
                        if (_owedCount >= _owedTier.Length)
                        {
                            overflow++;   // past the hard cap: reported with the pool shortfall below
                            continue;
                        }

                        _owedTier[_owedCount] = row.Tier;
                        _owedIsDebut[_owedCount] = false;
                        _owedCount++;
                    }
                }
            }

            // 3. Never more obligations than the pool can hold. Dropping is loud.
            if (_owedCount > _poolTotal || overflow > 0)
            {
                int dropped = Mathf.Max(0, _owedCount - _poolTotal) + overflow;
                _owedCount = Mathf.Min(_owedCount, _poolTotal);
                ShortfallCount += dropped;
                LastShortfallRound = _round;
                Debug.LogWarning("[Spawn] Round " + _round + ": " + dropped + " owed tier spawn(s) do not fit a pool of " + _poolTotal +
                                 " (REQ-071 debut / DEC-021 forced count). The pool is too small for the authored obligations.");
            }
        }

        /// <summary>
        /// Pick the tier of the next enemy. <paramref name="spawnedBefore"/> = enemies already
        /// released this round, this one excluded; <paramref name="remainingIncludingThis"/> =
        /// pool slots left, this one included (so the last slot of the round reads 1). They
        /// decide when an owed spawn can no longer wait. A pick that the caller then fails to
        /// activate must be handed back with <see cref="Return"/>: an obligation is spent by
        /// an activated enemy, never by a rolled-back reservation.
        /// </summary>
        public TierPick Next(PrototypeConfig config, int spawnedBefore, int remainingIncludingThis, IEnemyTierRandom random)
        {
            _picks++;
            int spawnedThisRound = spawnedBefore;
            int remainingInPool = remainingIncludingThis;

            int pending = _owedCount - _owedCursor;
            if (pending > 0)
            {
                bool pacingReached = spawnedThisRound >= _releaseAfterSpawns;
                bool runningOut = remainingInPool <= pending;
                if (pacingReached || runningOut)
                {
                    return Discharge(_owedCursor);
                }
            }

            EnemyTier drawn = Weighted(config, random);

            // A drawn tier that is still owed pays its own debt (a Runner that shows up
            // early by chance IS the round's Runner).
            int owedIndex = IndexOfOwed(drawn);
            if (owedIndex >= 0)
            {
                return Discharge(owedIndex);
            }

            return new TierPick(drawn, false, false);
        }

        /// <summary>
        /// Hand a pick back (the reservation it was drawn for was rolled back). An owed debut /
        /// forced count goes back to the FRONT of the queue so it is the next thing released
        /// (a chance-drawn debut handed back this way is re-queued ahead of the debuts still
        /// owed - deterministic, at most one spawn earlier than the config order); a plain
        /// weighted pick is simply forgotten. Idempotent for plain picks; a guaranteed pick is
        /// returned once per reservation (the director's two rollback branches are exclusive).
        /// </summary>
        public void Return(in TierPick pick)
        {
            if (_picks > 0)
            {
                _picks--;
            }

            if (!pick.Guaranteed && !pick.Forced)
            {
                return;
            }

            if (_owedCursor > 0)
            {
                _owedCursor--;
                _owedTier[_owedCursor] = pick.Tier;
                _owedIsDebut[_owedCursor] = pick.Guaranteed;
                return;
            }

            // Cursor at 0 cannot happen for a discharged pick (discharge advances it), but
            // never lose an obligation: append it instead.
            if (_owedCount < _owedTier.Length)
            {
                for (int j = _owedCount; j > 0; j--)
                {
                    _owedTier[j] = _owedTier[j - 1];
                    _owedIsDebut[j] = _owedIsDebut[j - 1];
                }

                _owedTier[0] = pick.Tier;
                _owedIsDebut[0] = pick.Guaranteed;
                _owedCount++;
            }
        }

        /// <summary>The weighted draw alone (no obligations). Exposed for the F1 debug fill so it reads the same authority.</summary>
        public EnemyTier Weighted(PrototypeConfig config, IEnemyTierRandom random)
        {
            if (config == null)
            {
                return EnemyTier.Standard;
            }

            int count = 0;
            float total = 0f;
            List<EnemyTierSettings> tiers = config.Enemies.Tiers;
            for (int i = 0; i < tiers.Count && count < _candidates.Length; i++)
            {
                EnemyTierSettings t = tiers[i];
                if (t == null || _round < t.IntroductionRound)
                {
                    continue;   // never before the introduction round, override or not
                }

                float weight = config.TierSpawnWeight(t.Tier, _round);
                if (weight <= 0f || float.IsNaN(weight) || float.IsInfinity(weight))
                {
                    continue;
                }

                _candidates[count] = t.Tier;
                _weights[count] = weight;
                total += weight;
                count++;
            }

            if (count == 0 || total <= 0f)
            {
                return EnemyTier.Standard;
            }

            float roll = (random != null ? random.Next01() : 0f) * total;
            for (int i = 0; i < count; i++)
            {
                roll -= _weights[i];
                if (roll <= 0f)
                {
                    return _candidates[i];
                }
            }

            return _candidates[count - 1];
        }

        /// <summary>Drop every obligation (run ended, round stopped). Idempotent.</summary>
        public void Clear()
        {
            _owedCount = 0;
            _owedCursor = 0;
            _picks = 0;
            _round = 0;
            _poolTotal = 0;
        }

        /// <summary>Pending obligation at <paramref name="index"/> (0 = next owed). For readouts and tests.</summary>
        public bool TryGetPending(int index, out EnemyTier tier, out bool isDebut)
        {
            int i = _owedCursor + index;
            if (index < 0 || i >= _owedCount)
            {
                tier = EnemyTier.Standard;
                isDebut = false;
                return false;
            }

            tier = _owedTier[i];
            isDebut = _owedIsDebut[i];
            return true;
        }

        private TierPick Discharge(int index)
        {
            EnemyTier tier = _owedTier[index];
            bool debut = _owedIsDebut[index];

            // Keep FIFO order for the rest: slide the entries before it one slot up and
            // park the discharged one at the cursor, which then moves past it.
            for (int j = index; j > _owedCursor; j--)
            {
                _owedTier[j] = _owedTier[j - 1];
                _owedIsDebut[j] = _owedIsDebut[j - 1];
            }

            _owedTier[_owedCursor] = tier;
            _owedIsDebut[_owedCursor] = debut;
            _owedCursor++;
            return new TierPick(tier, debut, !debut);
        }

        private int IndexOfOwed(EnemyTier tier)
        {
            for (int i = _owedCursor; i < _owedCount; i++)
            {
                if (_owedTier[i] == tier)
                {
                    return i;
                }
            }

            return -1;
        }

        private int CountOwed(EnemyTier tier)
        {
            int n = 0;
            for (int i = _owedCursor; i < _owedCount; i++)
            {
                if (_owedTier[i] == tier) n++;
            }

            return n;
        }

        private static bool IsIntroduced(PrototypeConfig config, EnemyTier tier, int round)
        {
            EnemyTierSettings t = config.Enemies.Find(tier);
            return t != null && round >= t.IntroductionRound;
        }
    }
}
