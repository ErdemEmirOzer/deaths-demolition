using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// The run's build as one value: which classic weapon, which tiers, what it cost.
    /// Rebuilt only when the build changes (see <see cref="RunBuildState"/>), so reading
    /// it every frame allocates nothing.
    /// </summary>
    public readonly struct RunBuildSnapshot
    {
        /// <summary>Run this build belongs to. 0 outside a run.</summary>
        public readonly int RunId;

        /// <summary>The classic weapon chosen at run start and carried for the whole run.</summary>
        public readonly ClassicWeaponId Weapon;

        /// <summary>Round in which the build last changed (purchase or reset). 0 for a stock build.</summary>
        public readonly int LastUpdatedRound;

        /// <summary>Cards bought this run.</summary>
        public readonly int PurchaseCount;

        /// <summary>Gold spent on cards this run.</summary>
        public readonly int TotalGoldSpent;

        /// <summary>Sum of owned tiers across every branch. 0 for a stock build.</summary>
        public readonly int TotalTiers;

        /// <summary>Rises with every build change. A copy that remembers an older value is stale.</summary>
        public readonly int Version;

        /// <summary>Weapon half plus player half, e.g. "P2 / FR1 / MS1". "Stock" for no upgrades.</summary>
        public readonly string BuildDescription;

        public RunBuildSnapshot(int runId, ClassicWeaponId weapon, int lastUpdatedRound, int purchaseCount,
            int totalGoldSpent, int totalTiers, int version, string buildDescription)
        {
            RunId = runId;
            Weapon = weapon;
            LastUpdatedRound = lastUpdatedRound;
            PurchaseCount = purchaseCount;
            TotalGoldSpent = totalGoldSpent;
            TotalTiers = totalTiers;
            Version = version;
            BuildDescription = buildDescription;
        }

        public bool IsStock => TotalTiers == 0;
    }

    /// <summary>
    /// The run's build, as an authoritative record of WHAT WAS BOUGHT in WHICH RUN.
    ///
    /// The owned tier of every branch stays where the weapon reads it, in
    /// <see cref="WeaponModifierState"/>; this class never keeps a second copy of a
    /// rank. It observes that state and adds what the state cannot know: the run id,
    /// the classic weapon, the round of the last change, the purchase count and the
    /// gold spent - and it caches a snapshot plus the full build description so the
    /// HUD, the results summary and the round-end screen read one value instead of
    /// rebuilding strings every frame.
    ///
    /// Lifecycle: <see cref="BeginRun"/> when a run starts (after the modifier state was
    /// wiped), <see cref="EndRun"/> when it ends. The Special Weapon is never described
    /// here - it does not read the modifier state and no card reaches it (REQ-059).
    ///
    /// Pure: no Unity lifecycle, no scene lookups; the director hands it the state.
    /// </summary>
    public sealed class RunBuildState
    {
        private readonly int[] _tierScratch = new int[WeaponModifierState.BranchCount];

        private WeaponModifierState _state;
        private RunBuildSnapshot _snapshot;
        private bool _snapshotDirty = true;
        private string _description;
        private int _version;

        public int RunId { get; private set; }

        public ClassicWeaponId Weapon { get; private set; }

        public int LastUpdatedRound { get; private set; }

        public int PurchaseCount { get; private set; }

        public int TotalGoldSpent { get; private set; }

        public bool IsRunLive => RunId > 0;

        /// <summary>True while attached to a live modifier state.</summary>
        public bool HasState => _state != null;

        /// <summary>Raised when the build changed (tier, purchase record, run boundary).</summary>
        public event Action<RunBuildSnapshot> Changed;

        // ------------------------------------------------------------------ binding

        /// <summary>Observes a modifier state. Replaces any previous one; a null detaches.</summary>
        public void Attach(WeaponModifierState state)
        {
            if (ReferenceEquals(_state, state))
            {
                return;
            }

            if (_state != null)
            {
                _state.Changed -= OnStateChanged;
            }

            _state = state;

            if (_state != null)
            {
                _state.Changed += OnStateChanged;
            }

            Touch();
        }

        public void Detach()
        {
            Attach(null);
        }

        // ------------------------------------------------------------------ run boundary

        /// <summary>Opens the build for a run. Counters start at zero; the tiers are whatever the state holds (wiped by the director first).</summary>
        public void BeginRun(int runId, ClassicWeaponId weapon)
        {
            RunId = runId > 0 ? runId : 1;
            Weapon = weapon;
            LastUpdatedRound = 0;
            PurchaseCount = 0;
            TotalGoldSpent = 0;
            Touch();
        }

        /// <summary>Closes the build. The state itself is reset by the director on the next run start.</summary>
        public void EndRun()
        {
            RunId = 0;
            LastUpdatedRound = 0;
            PurchaseCount = 0;
            TotalGoldSpent = 0;
            Touch();
        }

        /// <summary>A card was bought and its tier already applied. Records the spend against the run.</summary>
        public void RecordPurchase(UpgradeBranch branch, int cost, int round)
        {
            if (!IsRunLive)
            {
                return;
            }

            PurchaseCount++;
            TotalGoldSpent += Mathf.Max(0, cost);
            LastUpdatedRound = Mathf.Max(LastUpdatedRound, round);
            Touch();
        }

        /// <summary>WP-077 K10: the last card purchase was undone; the build readout drops it again.</summary>
        public void RecordPurchaseUndone(int cost)
        {
            if (!IsRunLive || PurchaseCount <= 0)
            {
                return;
            }

            PurchaseCount--;
            TotalGoldSpent = Mathf.Max(0, TotalGoldSpent - Mathf.Max(0, cost));
            Touch();
        }

        // ------------------------------------------------------------------ reads

        /// <summary>Owned tier of a branch, straight from the authoritative state. 0 when detached.</summary>
        public int Tier(UpgradeBranch branch)
        {
            return _state != null ? _state.GetTier(branch) : 0;
        }

        /// <summary>Sum of owned tiers. 0 for stock or detached.</summary>
        public int TotalTiers => _state != null ? _state.TotalTiers : 0;

        /// <summary>The cached snapshot. Rebuilt only after a change.</summary>
        public RunBuildSnapshot Snapshot
        {
            get
            {
                if (_snapshotDirty)
                {
                    _snapshot = new RunBuildSnapshot(RunId, Weapon, LastUpdatedRound, PurchaseCount,
                        TotalGoldSpent, TotalTiers, _version, DescribeBuild());
                    _snapshotDirty = false;
                }

                return _snapshot;
            }
        }

        /// <summary>
        /// Weapon half plus player half, e.g. "P2 / FR1 / MS1 / HP1"; "Stock" with no
        /// tiers. Cached until the next change - never rebuilt per frame.
        /// </summary>
        public string DescribeBuild()
        {
            if (_description != null)
            {
                return _description;
            }

            if (_state == null)
            {
                _description = "Stock";
                return _description;
            }

            _state.CopyTiersTo(_tierScratch);

            var sb = new System.Text.StringBuilder(64);
            Append(sb, "P", UpgradeBranch.Piercing);
            Append(sb, "R", UpgradeBranch.Ricochet);
            Append(sb, "X", UpgradeBranch.Explosion);
            Append(sb, "C", UpgradeBranch.Chain);
            Append(sb, "FR", UpgradeBranch.FireRate);
            Append(sb, "RL", UpgradeBranch.ReloadSpeed);
            Append(sb, "MS", UpgradeBranch.MovementSpeed);
            Append(sb, "HP", UpgradeBranch.MaximumHealth);
            Append(sb, "RD", UpgradeBranch.RegenerationDelay);
            Append(sb, "RR", UpgradeBranch.RegenerationRate);

            _description = sb.Length == 0 ? "Stock" : sb.ToString();
            return _description;
        }

        // ------------------------------------------------------------------ internals

        private void Append(System.Text.StringBuilder sb, string prefix, UpgradeBranch branch)
        {
            int tier = _tierScratch[(int)branch];
            if (tier <= 0)
            {
                return;
            }

            if (sb.Length > 0)
            {
                sb.Append(" / ");
            }

            sb.Append(prefix).Append(tier);
        }

        private void OnStateChanged(WeaponModifierState state)
        {
            Touch();
        }

        private void Touch()
        {
            _version++;
            _description = null;
            _snapshotDirty = true;

            Action<RunBuildSnapshot> handler = Changed;
            if (handler != null)
            {
                handler.Invoke(Snapshot);
            }
        }
    }
}
