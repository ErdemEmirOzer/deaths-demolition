using DD.Prototype.Core.Config;

namespace DD.Prototype.Spawn
{
    /// <summary>
    /// REQ-071 / DEC-021 - the director's tier authority (WP-010, ADR-010-1). This is the
    /// thin, stateful shell the <see cref="EnemySpawnDirector"/> owns: it holds the one
    /// <see cref="TierSelectionPolicy"/> of the run and the one source of chance, and turns
    /// the director's ledger numbers into policy calls. Every rule (debut guarantee and its
    /// pacing, pool-running-out forcing, DEC-021 forced counts, the weighted draw, the
    /// "never before the introduction round" filter) lives in the policy, which is pure
    /// and EditMode-testable. The WP-000 <c>EnemyTierScheduler</c>, which claimed the same
    /// requirement with a second semantic and was never reached by a real run, is gone.
    ///
    /// The source of chance is injectable (<see cref="IEnemyTierRandom"/>); the shipped
    /// source is <see cref="UnityTierRandomSource"/>, i.e. <c>UnityEngine.Random.value</c>,
    /// the same stream the WP-003 selector drew from, so the production distribution is
    /// unchanged. A test or the development probe injects a seeded source and gets a
    /// reproducible round composition.
    /// </summary>
    public sealed class SpawnTierSelector
    {
        private readonly TierSelectionPolicy _policy = new TierSelectionPolicy();
        private IEnemyTierRandom _random = UnityTierRandomSource.Instance;

        /// <summary>The policy (read-only view for readouts, probes and tests).</summary>
        public TierSelectionPolicy Policy => _policy;

        /// <summary>The source of chance in use. Production: <see cref="UnityTierRandomSource"/>.</summary>
        public IEnemyTierRandom Random => _random;

        /// <summary>Tiers still owed a guaranteed debut this round.</summary>
        public int PendingGuaranteed => _policy.PendingGuaranteed;

        /// <summary>Debuts and forced counts still owed this round.</summary>
        public int PendingObligations => _policy.PendingObligations;

        /// <summary>
        /// Inject the source of chance (tests, probe). Null restores the shipped source.
        /// Takes effect from the next draw; the owed list is untouched.
        /// </summary>
        public void SetRandom(IEnemyTierRandom random)
        {
            _random = random ?? UnityTierRandomSource.Instance;
        }

        public void BeginRound(int round, PrototypeConfig config, int poolTotal, float releaseFraction)
        {
            _policy.BeginRound(round, config, poolTotal, releaseFraction);
        }

        /// <summary>
        /// Picks the tier of the next enemy. <paramref name="spawnedBefore"/> = released this
        /// round before this one; <paramref name="remainingIncludingThis"/> = pool slots left,
        /// this one included. Hand the pick back with <see cref="Return"/> if the spawn it was
        /// drawn for is rolled back.
        /// </summary>
        public TierPick Next(PrototypeConfig config, int spawnedBefore, int remainingIncludingThis)
        {
            return _policy.Next(config, spawnedBefore, remainingIncludingThis, _random);
        }

        /// <summary>The reservation this pick was drawn for was rolled back: its obligation (if any) is owed again.</summary>
        public void Return(in TierPick pick)
        {
            _policy.Return(in pick);
        }

        /// <summary>The weighted draw alone, for the F1 debug fill (same authority, same weights).</summary>
        public EnemyTier DrawWeighted(PrototypeConfig config)
        {
            return _policy.Weighted(config, _random);
        }

        /// <summary>Drop every obligation (run ended / round stopped). Idempotent.</summary>
        public void Clear()
        {
            _policy.Clear();
        }

        /// <summary>Debug readout: which debuts / forced counts are still owed. Builds a string - F1 panel only.</summary>
        public string DescribePending()
        {
            int n = _policy.PendingObligations;
            if (n == 0)
            {
                return "none";
            }

            string result = "";
            for (int i = 0; i < n; i++)
            {
                if (_policy.TryGetPending(i, out EnemyTier tier, out bool debut))
                {
                    result += (i > 0 ? ", " : "") + tier + (debut ? " (debut)" : " (forced)");
                }
            }

            return result;
        }

        /// <summary>One-line state for the F1 "Tier schedule" readout. Builds a string - F1 panel only.</summary>
        public string Describe()
        {
            return "round " + _policy.Round + "  picks " + _policy.Picks + "/" + _policy.PoolTotal +
                   "  owed " + _policy.PendingObligations + " (debut " + _policy.PendingGuaranteed + ")" +
                   (_policy.ShortfallCount > 0 ? "  SHORTFALL " + _policy.ShortfallCount + " (round " + _policy.LastShortfallRound + ")" : "");
        }
    }
}
