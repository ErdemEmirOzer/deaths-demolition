using System;
using DD.Prototype.Rounds;
using UnityEngine;

namespace DD.Prototype.Economy
{
    /// <summary>
    /// The run's gold. One number, three rules, and no way around them:
    ///
    ///   * gold exists only inside a run (REQ-018) - <see cref="ResetForNewRun"/> is
    ///     called by the run director when a run ends AND when one starts;
    ///   * gold never becomes XP - this class has no reference to anything that grants
    ///     XP and never returns an XP value;
    ///   * unspent gold carries between rounds (request) - nothing in the round
    ///     transition touches the balance, which is the whole implementation of that
    ///     rule.
    ///
    /// A plain class rather than a static, so the shop and the director can be handed
    /// the same instance while a test builds its own.
    /// </summary>
    public sealed class GoldWallet
    {
        /// <summary>Current spendable balance.</summary>
        public int Balance { get; private set; }

        /// <summary>Everything credited this run, including gold already spent.</summary>
        public int TotalEarned { get; private set; }

        /// <summary>Everything spent this run.</summary>
        public int TotalSpent { get; private set; }

        /// <summary>Raised on any balance change. Argument is the new balance.</summary>
        public event Action<int> Changed;

        /// <summary>Raised when gold arrives. Arguments: amount, new balance, world position.</summary>
        public event Action<int, int, Vector3> Credited;

        public bool CanAfford(int cost) => cost <= Balance;

        /// <summary>Add gold. <paramref name="position"/> is only carried through for feedback.</summary>
        public void Credit(int amount, Vector3 position = default)
        {
            if (amount <= 0)
            {
                return;
            }

            Balance += amount;
            TotalEarned += amount;

            Credited?.Invoke(amount, Balance, position);
            RaiseChanged();
        }

        /// <summary>Spend gold. Returns false and changes nothing when the balance is short.</summary>
        public bool TrySpend(int cost)
        {
            if (cost <= 0 || cost > Balance)
            {
                return false;
            }

            Balance -= cost;
            TotalSpent += cost;

            RaiseChanged();
            return true;
        }

        /// <summary>Debug only. The F1 panel's "give gold" button.</summary>
        public void DebugSet(int balance)
        {
            Balance = Mathf.Max(0, balance);
            RaiseChanged();
        }

        /// <summary>Wipes the wallet. Death destroys the run's gold; nothing carries over.</summary>
        public void ResetForNewRun()
        {
            bool changed = Balance != 0 || TotalEarned != 0 || TotalSpent != 0;

            Balance = 0;
            TotalEarned = 0;
            TotalSpent = 0;

            if (changed)
            {
                RaiseChanged();
            }
        }

        private void RaiseChanged()
        {
            // The HUD snapshot is updated here rather than on the director's next frame,
            // so a collected orb and the counter animation land in the same frame.
            RunEvents.PublishGold(Balance);

            Changed?.Invoke(Balance);
        }
    }
}
