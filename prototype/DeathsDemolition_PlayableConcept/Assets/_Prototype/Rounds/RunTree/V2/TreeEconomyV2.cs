using System;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// WP-104 B1 · the tree's kill-gold scale: Coin Sense (<see cref="TreeStats.GoldScale"/>, tree × meta capped ×1,60). The mode's
    /// gold (decision 5: Hard ×1,25, Demolition ×2,5 + 150 start) is WP-102's DifficultyService.ScaleGold, applied before this one in
    /// GoldService. A fractional carry keeps small packets honest (2 gold × 1,1 pays 2, 2, … 3) and stays deterministic; no tree = unchanged.
    /// </summary>
    public static class TreeEconomyV2
    {
        private static float s_carry;

        /// <summary>The tree's gold scale now (1 without Coin Sense).</summary>
        public static float GoldScale => TreeRuntime.Current.GoldScale;

        /// <summary>The gold a kill pays after the scale. Exactly <paramref name="total"/> when the scale is 1.</summary>
        public static int ScaleKillGold(int total)
        {
            int paid = ScaleKillGold(total, out float carry);
            CommitCarry(carry);
            return paid;
        }

        /// <summary>As <see cref="ScaleKillGold(int)"/> without moving the carry: GoldService commits it only for a granted kill
        /// (the WP-102 pattern — a refused claim must not eat the fraction).</summary>
        public static int ScaleKillGold(int total, out float carryAfter)
        {
            carryAfter = s_carry;
            if (total <= 0) return total;
            float scale = GoldScale;
            if (Mathf.Approximately(scale, 1f)) return total;
            float exact = total * scale + s_carry;
            int paid = Mathf.FloorToInt(exact + 1e-4f); // float noise must not drop a whole coin
            carryAfter = exact - paid;
            return Math.Max(0, paid);
        }

        public static void CommitCarry(float carry) => s_carry = carry;

        /// <summary>Run boundary / tests.</summary>
        public static void ResetCarry() => s_carry = 0f;
    }
}
