using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Core.Config
{
    /// <summary>
    /// Single central data layer for every PROTOTYPE-ONLY number in the build.
    ///
    /// LIVE CONTRACT
    /// -------------
    /// This asset is mutable at runtime. Any edit - from the Inspector during play or
    /// from the F1 debug panel - must reach the systems that use it within the same
    /// frame. Therefore:
    ///
    ///   * Consumers MUST NOT copy values into private fields in Awake and forget them.
    ///   * A consumer either reads config.Section.Field directly every time it needs
    ///     the value, or it caches and subscribes to <see cref="Changed"/> (see
    ///     <see cref="PrototypeConfigBehaviour"/>), or it polls <see cref="Revision"/>
    ///     through a <see cref="ConfigRevisionWatcher"/> in a hot loop.
    ///   * Anything that derives state from config (spawn schedules, weapon timers,
    ///     max health) recomputes that state inside its ApplyConfig callback.
    ///
    /// Nothing in this asset is a product decision. See PROTOTYPE-ONLY.md.
    /// </summary>
    [CreateAssetMenu(fileName = "PrototypeConfig", menuName = "Deaths Demolition/Prototype Config", order = 0)]
    public sealed class PrototypeConfig : ScriptableObject
    {
        /// <summary>Path under any Resources folder. Boot loads the asset from here.</summary>
        public const string ResourcesPath = "PrototypeConfig";

        // ------------------------------------------------------------------ sections

        [SerializeField] private PlayerConfigSection _player = new PlayerConfigSection();
        [SerializeField] private CameraConfigSection _camera = new CameraConfigSection();
        [SerializeField] private WeaponConfigSection _weapons = new WeaponConfigSection();
        [SerializeField] private EnemyConfigSection _enemies = new EnemyConfigSection();
        [SerializeField] private RoundConfigSection _rounds = new RoundConfigSection();
        [SerializeField] private SpawnConfigSection _spawn = new SpawnConfigSection();
        [SerializeField] private EconomyConfigSection _economy = new EconomyConfigSection();
        [SerializeField] private ProgressionConfigSection _progression = new ProgressionConfigSection();
        [SerializeField] private FeedbackConfigSection _feedback = new FeedbackConfigSection();
        [SerializeField] private SystemConfigSection _system = new SystemConfigSection();

        /// <summary>
        /// DEC-021 SKELETON - INTENTIONALLY EMPTY.
        /// DEC-021 wants hybrid round scaling: formula plus hand-authored armour ratios
        /// and tier distributions on specific rounds. This run implements the formula
        /// half only (t-0002). The list exists so the authored half is visible and not
        /// contradicted; it must stay empty until that half is explicitly commissioned.
        /// </summary>
        [SerializeField] private List<RoundDifficultyOverride> _roundDifficultyOverrides
            = new List<RoundDifficultyOverride>();

        public PlayerConfigSection Player => _player;
        public CameraConfigSection Camera => _camera;
        public WeaponConfigSection Weapons => _weapons;
        public EnemyConfigSection Enemies => _enemies;
        public RoundConfigSection Rounds => _rounds;
        public SpawnConfigSection Spawn => _spawn;
        public EconomyConfigSection Economy => _economy;
        public ProgressionConfigSection Progression => _progression;
        public FeedbackConfigSection Feedback => _feedback;
        public SystemConfigSection System => _system;

        /// <summary>DEC-021 authored overrides. Empty by design in this prototype.</summary>
        public IReadOnlyList<RoundDifficultyOverride> RoundDifficultyOverrides => _roundDifficultyOverrides;

        // ------------------------------------------------------------- live plumbing

        [NonSerialized] private int _revision;

        /// <summary>
        /// Raised whenever any value changed. Subscribers must re-read what they cache.
        /// Fires on the main thread only.
        /// </summary>
        public event Action<PrototypeConfig> Changed;

        /// <summary>Increments on every change. Cheap way to poll without an event.</summary>
        public int Revision => _revision;

        /// <summary>
        /// Announce that values changed. The F1 panel calls this after every edit;
        /// OnValidate calls it after every Inspector edit.
        /// </summary>
        public void NotifyChanged()
        {
            unchecked
            {
                _revision++;
            }

            Action<PrototypeConfig> handler = Changed;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke(this);
            }
            catch (Exception e)
            {
                Debug.LogException(e, this);
            }
        }

        private void OnValidate()
        {
            NotifyChanged();
        }

        // ----------------------------------------------------------------- active asset

        private static PrototypeConfig s_active;

        /// <summary>Raised when the active asset instance itself is swapped.</summary>
        public static event Action<PrototypeConfig> ActiveChanged;

        /// <summary>
        /// The config every system reads unless it was handed an explicit override.
        /// Boot assigns it; falls back to a Resources load so a scene opened directly
        /// in the editor still works.
        /// </summary>
        public static PrototypeConfig Active
        {
            get
            {
                if (s_active == null)
                {
                    s_active = Resources.Load<PrototypeConfig>(ResourcesPath);
                }

                return s_active;
            }
        }

        public static void SetActive(PrototypeConfig config)
        {
            if (s_active == config)
            {
                return;
            }

            s_active = config;
            ActiveChanged?.Invoke(config);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_active = null;
            ActiveChanged = null;
        }

        // -------------------------------------------------------------------- formulas
        // All of these are the request's PROTOTYPE-ONLY formulas, driven by the live
        // coefficients above. They are the single source of truth for round scaling.

        public int TotalEnemyPool(int round) => _rounds.TotalEnemyPool(round);

        public int TargetActiveEnemies(int round) => _rounds.TargetActiveEnemies(round);

        public int AbsoluteSafetyCeiling => _rounds.AbsoluteSafetyCeiling;

        public float EnemyHealthMultiplier(int round) => _rounds.EnemyHealthMultiplier(round);

        public float EnemyDamageMultiplier(int round) => _rounds.EnemyDamageMultiplier(round);

        /// <summary>Scaled health of one enemy of the given tier in the given round.</summary>
        public float EnemyHealth(EnemyTier tier, int round)
        {
            EnemyTierSettings t = _enemies.Find(tier);
            float mul = t != null ? t.HealthMultiplier : 1f;
            return _enemies.StandardBaseHealth * mul * EnemyHealthMultiplier(round);
        }

        /// <summary>Scaled attack damage of one enemy of the given tier in the given round.</summary>
        public float EnemyAttackDamage(EnemyTier tier, int round)
        {
            EnemyTierSettings t = _enemies.Find(tier);
            float dmg = t != null ? t.AttackDamage : 0f;
            return dmg * EnemyDamageMultiplier(round);
        }

        /// <summary>
        /// Chance that a tier spawns wearing the given armour piece this round.
        /// Applies a DEC-021 authored override when one exists - it never does today.
        /// </summary>
        public float ArmorChance(EnemyTier tier, ArmorSlot slot, int round)
        {
            if (TryGetRoundOverride(round, out RoundDifficultyOverride ovr) && ovr.OverrideArmorChances)
            {
                return ovr.ArmorChances.Resolve(slot, round);
            }

            EnemyTierSettings t = _enemies.Find(tier);
            return t != null ? t.ArmorChances.Resolve(slot, round) : 0f;
        }

        /// <summary>
        /// Selection weight of a tier this round. Applies a DEC-021 authored override
        /// when one exists - it never does today.
        /// </summary>
        public float TierSpawnWeight(EnemyTier tier, int round)
        {
            if (TryGetRoundOverride(round, out RoundDifficultyOverride ovr) && ovr.OverrideTierWeights)
            {
                for (int i = 0; i < ovr.TierWeights.Count; i++)
                {
                    if (ovr.TierWeights[i] != null && ovr.TierWeights[i].Tier == tier)
                    {
                        return ovr.TierWeights[i].Weight;
                    }
                }

                return 0f;
            }

            EnemyTierSettings t = _enemies.Find(tier);
            return t != null ? t.ResolveSpawnWeight(round) : 0f;
        }

        /// <summary>Probability that a Special Weapon appears at all in the given round.</summary>
        public float SpecialWeaponSpawnChance(int round)
        {
            SpecialWeaponConfigSection s = _weapons.Special;
            if (round < s.FirstEligibleRound)
            {
                return 0f;
            }

            int steps = Mathf.Max(0, round - s.FirstEligibleRound);
            return Mathf.Min(s.BaseSpawnChance + s.SpawnChanceGrowthPerRound * steps, s.MaxSpawnChance);
        }

        public int RoundCompletionXp(int round) => _progression.RoundCompletionXp(round);

        public int LevelRequirement(int level) => _progression.LevelRequirement(level);

        /// <summary>Total XP from one kill: flat kill XP plus the tier bonus.</summary>
        public int KillXp(EnemyTier tier)
        {
            EnemyTierSettings t = _enemies.Find(tier);
            return _progression.XpPerKill + (t != null ? t.BonusKillXp : 0);
        }

        public int GoldReward(EnemyTier tier)
        {
            EnemyTierSettings t = _enemies.Find(tier);
            return t != null ? t.GoldReward : 0;
        }

        /// <summary>
        /// DEC-021 lookup. Returns false for every round while the override list is
        /// empty, which is the intended state of this prototype.
        /// </summary>
        public bool TryGetRoundOverride(int round, out RoundDifficultyOverride result)
        {
            for (int i = 0; i < _roundDifficultyOverrides.Count; i++)
            {
                RoundDifficultyOverride o = _roundDifficultyOverrides[i];
                if (o != null && o.Round == round)
                {
                    result = o;
                    return true;
                }
            }

            result = null;
            return false;
        }
    }
}
