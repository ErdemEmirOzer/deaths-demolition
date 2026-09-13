using System;
using System.Collections.Generic;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Economy
{
    /// <summary>
    /// The run's gold. One number, three rules, and no way around them:
    ///
    ///   * gold exists only inside a run (REQ-018) - <see cref="InitializeForRun"/> opens
    ///     the wallet when a run starts and <see cref="ResetForRunEnd"/> destroys what is
    ///     left when it ends; a transaction outside a run is refused;
    ///   * gold never becomes XP - this class has no reference to anything that grants
    ///     XP and never returns an XP value;
    ///   * unspent gold carries between rounds (request) - nothing in the round
    ///     transition touches the balance, which is the whole implementation of that
    ///     rule.
    ///
    /// WP-004 made it a ledger rather than a counter: every movement is a transaction
    /// with a <see cref="GoldSource"/> and an id, an id is applied at most once per run,
    /// a negative amount or an unaffordable spend changes nothing, and every change is
    /// published as one <see cref="GoldWalletSnapshot"/> so a screen never keeps a
    /// balance of its own.
    ///
    /// A plain class rather than a static, so the shop and the director can be handed
    /// the same instance while a test builds its own.
    /// </summary>
    public sealed class GoldWallet
    {
        /// <summary>Transaction ids already applied this run. Cleared with the run.</summary>
        private readonly HashSet<int> _applied = new HashSet<int>(256);

        private int _nextTransactionId = 1;
        private int _version;

        /// <summary>Run this wallet is open for. 0 while no run is open.</summary>
        public int RunId { get; private set; }

        /// <summary>True between <see cref="InitializeForRun"/> and a reset.</summary>
        public bool IsOpen => RunId > 0;

        /// <summary>Current spendable balance.</summary>
        public int Balance { get; private set; }

        /// <summary>Everything credited this run, including gold already spent.</summary>
        public int TotalEarned { get; private set; }

        /// <summary>Everything spent this run.</summary>
        public int TotalSpent { get; private set; }

        /// <summary>Gold that arrived through packets the player collected.</summary>
        public int EarnedByPickup { get; private set; }

        /// <summary>Gold that arrived through round-end auto-collection.</summary>
        public int EarnedByAutoCollect { get; private set; }

        /// <summary>Gold spent on upgrade cards (WP-008). <see cref="TotalSpent"/> = this + <see cref="SpentOnRerolls"/> for the two product spend sources.</summary>
        public int SpentOnUpgrades { get; private set; }

        /// <summary>Gold spent on paid offer rerolls (WP-008, DEC-050).</summary>
        public int SpentOnRerolls { get; private set; }

        /// <summary>Accepted transactions this run. F1 readout and test evidence.</summary>
        public int AppliedTransactions => _applied.Count;

        /// <summary>The wallet as one value. Equal to what the last <see cref="SnapshotChanged"/> carried.</summary>
        public GoldWalletSnapshot Snapshot =>
            new GoldWalletSnapshot(RunId, Balance, TotalEarned, TotalSpent, EarnedByPickup, EarnedByAutoCollect,
                SpentOnUpgrades, SpentOnRerolls, _version);

        /// <summary>Raised on any balance change. Argument is the new balance.</summary>
        public event Action<int> Changed;

        /// <summary>Raised when gold arrives. Arguments: amount, new balance, world position.</summary>
        public event Action<int, int, Vector3> Credited;

        /// <summary>Raised after every accepted transaction and every reset, with the full snapshot.</summary>
        public event Action<GoldWalletSnapshot> SnapshotChanged;

        public bool CanAfford(int cost) => cost <= Balance;

        // ------------------------------------------------------------------ run boundary

        /// <summary>
        /// Opens the wallet for a run at the default run balance (zero). Called by the
        /// run director when a run starts; a run can never inherit the previous one's
        /// balance because everything is wiped here first.
        /// </summary>
        public void InitializeForRun(int runId)
        {
            Wipe();
            RunId = Mathf.Max(1, runId);
            RaiseChanged();
        }

        /// <summary>Destroys the run's gold. Death ends the run and nothing carries over.</summary>
        public void ResetForRunEnd()
        {
            bool changed = Balance != 0 || TotalEarned != 0 || TotalSpent != 0 || RunId != 0;

            Wipe();

            if (changed)
            {
                RaiseChanged();
            }
        }

        /// <summary>
        /// Wipes the wallet without opening a run. Kept for the run director's "wipe on
        /// start as well as on end" belt-and-braces and for the F1 panel.
        /// </summary>
        public void ResetForNewRun()
        {
            ResetForRunEnd();
        }

        /// <summary>A fresh id for a transaction this wallet has not seen. Never repeats within a run.</summary>
        public int NextTransactionId()
        {
            return _nextTransactionId++;
        }

        // ------------------------------------------------------------------ transactions

        /// <summary>
        /// Add gold. Refused (and nothing changes) for a non-positive amount, an id this
        /// run already applied, or a wallet with no run open.
        /// </summary>
        public GoldTransactionResult Credit(int amount, GoldSource source, int transactionId, Vector3 position = default)
        {
            GoldTransactionResult result = ApplyCredit(amount, transactionId);
            if (result != GoldTransactionResult.Applied)
            {
                return result;
            }

            switch (source)
            {
                case GoldSource.EnemyPickup:
                    EarnedByPickup = (int)Math.Min((long)EarnedByPickup + amount, int.MaxValue);
                    break;
                case GoldSource.RoundClearAutoCollect:
                    EarnedByAutoCollect = (int)Math.Min((long)EarnedByAutoCollect + amount, int.MaxValue);
                    break;
            }

            Publish(amount, position);
            return GoldTransactionResult.Applied;
        }

        /// <summary>
        /// Untagged credit: the F1 "give gold" buttons and the lifecycle tests. Counts
        /// toward the balance and TotalEarned like any credit, but toward no product
        /// source, so the per-source totals stay honest. Fresh id; refused outside a run.
        /// </summary>
        public bool Credit(int amount, Vector3 position = default)
        {
            if (ApplyCredit(amount, NextTransactionId()) != GoldTransactionResult.Applied)
            {
                return false;
            }

            Publish(amount, position);
            return true;
        }

        /// <summary>
        /// Spend gold. Returns <see cref="GoldTransactionResult.Applied"/> and takes the
        /// cost exactly once; any other result leaves the balance untouched.
        /// </summary>
        public GoldTransactionResult TrySpend(int cost, GoldSource source, int transactionId)
        {
            if (!IsOpen)
            {
                return GoldTransactionResult.NoRun;
            }

            if (cost <= 0)
            {
                return GoldTransactionResult.InvalidAmount;
            }

            if (_applied.Contains(transactionId))
            {
                return GoldTransactionResult.Duplicate;
            }

            if (cost > Balance)
            {
                return GoldTransactionResult.InsufficientBalance;
            }

            _applied.Add(transactionId);

            Balance -= cost;
            TotalSpent += cost;

            switch (source)
            {
                case GoldSource.UpgradePurchase:
                    SpentOnUpgrades += cost;
                    break;
                case GoldSource.OfferReroll:
                    SpentOnRerolls += cost;
                    break;
            }

            _version++;
            RaiseChanged();
            return GoldTransactionResult.Applied;
        }

        /// <summary>Spend gold with a fresh id. Returns false and changes nothing when the balance is short.</summary>
        public bool TrySpend(int cost)
        {
            return TrySpend(cost, GoldSource.UpgradePurchase, NextTransactionId()) == GoldTransactionResult.Applied;
        }

        /// <summary>Debug only. The F1 panel's "clear wallet" button. Outside a run it does nothing.</summary>
        public void DebugSet(int balance)
        {
            if (!IsOpen)
            {
                return;
            }

            Balance = Mathf.Max(0, balance);
            _version++;
            RaiseChanged();
        }

        // ------------------------------------------------------------------ internals

        private GoldTransactionResult ApplyCredit(int amount, int transactionId)
        {
            if (!IsOpen)
            {
                return GoldTransactionResult.NoRun;
            }

            if (amount <= 0)
            {
                return GoldTransactionResult.InvalidAmount;
            }

            if (!_applied.Add(transactionId))
            {
                return GoldTransactionResult.Duplicate;
            }

            // Saturating, never wrapping: an absurd credit can cap the wallet but can never
            // turn the balance negative (WP-008 overflow rule).
            Balance = (int)Math.Min((long)Balance + amount, int.MaxValue);
            TotalEarned = (int)Math.Min((long)TotalEarned + amount, int.MaxValue);
            _version++;
            return GoldTransactionResult.Applied;
        }

        private void Publish(int amount, Vector3 position)
        {
            Credited?.Invoke(amount, Balance, position);
            RaiseChanged();
        }

        private void Wipe()
        {
            RunId = 0;
            Balance = 0;
            TotalEarned = 0;
            TotalSpent = 0;
            EarnedByPickup = 0;
            EarnedByAutoCollect = 0;
            SpentOnUpgrades = 0;
            SpentOnRerolls = 0;
            _version = 0;
            _nextTransactionId = 1;
            _applied.Clear();
        }

        private void RaiseChanged()
        {
            // The HUD snapshot is updated here rather than on the director's next frame,
            // so a collected packet and the counter animation land in the same frame.
            RunEvents.PublishGold(Balance);

            Changed?.Invoke(Balance);
            SnapshotChanged?.Invoke(Snapshot);
        }
    }
}
