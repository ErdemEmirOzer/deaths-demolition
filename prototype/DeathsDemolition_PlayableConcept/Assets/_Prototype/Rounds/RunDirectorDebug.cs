using System;
using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Economy;
using UnityEngine;

namespace DD.Prototype.Rounds
{
    /// <summary>
    /// The F1 half of <see cref="RunDirector"/>. Split into its own file because the run
    /// lifecycle should be readable without scrolling past thirty debug rows, not because
    /// the panel is optional - the request names round skipping, giving gold, switching
    /// the gold mode and forcing the upgrade screen as required controls.
    ///
    /// Everything here goes through the same public methods the game uses. There is no
    /// debug-only path into the round flow, so a bug found with a skip button is a bug
    /// that exists in normal play too.
    /// </summary>
    public sealed partial class RunDirector
    {
        private DebugPanelEntry[] _debugEntries = Array.Empty<DebugPanelEntry>();
        private int _debugSkipTarget = 5;
        private int _debugGoldGrant = 100;

        private void RegisterDebugPanel()
        {
            var entries = new List<DebugPanelEntry>(24);

            // ---- readouts ----------------------------------------------------
            entries.Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Rounds,
                "Run", DescribeRun, -100));

            entries.Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Rounds,
                "Round", DescribeRound, -99));

            entries.Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Rounds,
                "Clock", () => RunFlowClock.DescribeHolds(), -98));

            entries.Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Rounds,
                "XP (no gold, no playtime)", DescribeXp, -97));

            entries.Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Rounds,
                "Special Weapon bridge", DescribeBridge, -96));

            // ---- run ---------------------------------------------------------
            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Rounds,
                "Start / restart run at round 1", () => StartRun(1), -90));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Rounds,
                "End run (debug)", () => EndRun(RunEndReason.Debug, "F1 panel"), -89));

            // ---- round skipping ----------------------------------------------
            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Rounds,
                "Skip to next round (no upgrade screen)", SkipToNextRound, -80));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Rounds,
                "Complete this round (full round-end flow)", ForceCompleteRound, -79));

            entries.Add(DebugPanelRegistry.AddSlider(DebugPanelTabs.Rounds,
                "Skip target round",
                () => _debugSkipTarget,
                v => _debugSkipTarget = Mathf.Max(1, Mathf.RoundToInt(v)),
                1f, 25f, true, -78));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Rounds,
                "Skip to round N", () => SkipToRound(_debugSkipTarget), -77));

            // ---- upgrade screen ----------------------------------------------
            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Rounds,
                "Force upgrade screen open", ForceUpgradeScreen, -70));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Rounds,
                "Close upgrade screen (start countdown)", CloseUpgradeScreen, -69));

            entries.Add(DebugPanelRegistry.AddToggle(DebugPanelTabs.Rounds,
                "Show upgrade screen at round end",
                () => _showUpgradeScreen,
                v => _showUpgradeScreen = v, -68));

            entries.Add(DebugPanelRegistry.AddToggle(DebugPanelTabs.Rounds,
                "Offer unbacked branches (" + UpgradeCardCatalog.DescribeUnbacked() + ")",
                () => _shop.OfferUnbackedBranches,
                v => _shop.OfferUnbackedBranches = v, -67));

            entries.Add(DebugPanelRegistry.AddSection(DebugPanelTabs.Rounds,
                "Upgrade offer (live)", DrawOffer, -60));

            // ---- gold, mirrored here so the round tab is self-sufficient -------
            entries.Add(DebugPanelRegistry.AddSlider(DebugPanelTabs.Rounds,
                "Gold grant amount",
                () => _debugGoldGrant,
                v => _debugGoldGrant = Mathf.Max(1, Mathf.RoundToInt(v)),
                1f, 500f, true, -50));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Rounds,
                "Give gold", () => GiveGold(_debugGoldGrant), -49));

            entries.Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Rounds,
                "Cycle gold collection mode", CycleGoldMode, -48));

            _debugEntries = entries.ToArray();
        }

        private void UnregisterDebugPanel()
        {
            for (int i = 0; i < _debugEntries.Length; i++)
            {
                DebugPanelRegistry.Remove(_debugEntries[i]);
            }

            _debugEntries = Array.Empty<DebugPanelEntry>();
        }

        // ------------------------------------------------------------------ actions

        /// <summary>
        /// Jumps straight into the next round. Deliberately skips the round-end flow, so it
        /// is the fast way to reach round 12; use "Complete this round" to test the flow.
        /// </summary>
        private void SkipToNextRound()
        {
            SkipToRound(_round + 1);
        }

        private void SkipToRound(int round)
        {
            if (!_runEverStarted)
            {
                StartRun(round);
                return;
            }

            if (_phase == RunPhase.Dead)
            {
                StartRun(round);
                return;
            }

            RunFlowClock.RemoveHold(RunFlowClock.RoundEndHold);
            BeginRound(round);
        }

        /// <summary>
        /// Empties the current round the honest way: stop the pool and clear the map, then
        /// let the spawn director raise RoundCleared exactly as it would in play. The
        /// round-end freeze, the upgrade screen and the countdown all follow by themselves.
        /// </summary>
        private void ForceCompleteRound()
        {
            if (_phase != RunPhase.Fighting)
            {
                return;
            }

            Spawn.EnemySpawnDirector spawn = ResolveSpawnDirector();
            if (spawn == null)
            {
                // No spawn director in the scene: drive the completion directly so the
                // round flow is still testable in an empty scene.
                OnRoundCleared(_round);
                return;
            }

            // The director drains its pool, clears the map and completes through its own
            // ledger; RoundPoolExhausted and RoundCleared arrive here through the normal
            // binding. Only a director with no live round needs the direct path.
            if (!spawn.DebugCompleteRound())
            {
                OnRoundPoolExhausted(_round);
                OnRoundCleared(_round);
            }
        }

        private void ForceUpgradeScreen()
        {
            if (_phase == RunPhase.Upgrading)
            {
                return;
            }

            if (_phase == RunPhase.None || _phase == RunPhase.Dead)
            {
                return;
            }

            OpenUpgradeScreen();
        }

        private void GiveGold(int amount)
        {
            GoldWallet wallet = Wallet;
            if (wallet != null)
            {
                wallet.Credit(amount);
            }
        }

        private static void CycleGoldMode()
        {
            GoldService gold = GoldService.Instance;
            if (gold == null)
            {
                return;
            }

            PrototypeGoldCollectionMode next;
            switch (gold.Mode)
            {
                case PrototypeGoldCollectionMode.DirectCredit:
                    next = PrototypeGoldCollectionMode.ContactPickup;
                    break;
                case PrototypeGoldCollectionMode.ContactPickup:
                    next = PrototypeGoldCollectionMode.ProximityMagnet;
                    break;
                default:
                    next = PrototypeGoldCollectionMode.DirectCredit;
                    break;
            }

            gold.SetMode(next);
        }

        // ------------------------------------------------------------------ readouts

        /// <summary>
        /// Rounds done and armour broken are shown because they are the two summary fields
        /// with no other on-screen witness before the results screen: both feed a result
        /// line (round XP) and an achievement (Armor Breaker), and both are silent when
        /// wrong. If either stays at 0 through a cleared round or a broken helmet, the
        /// counting is broken, and this row is where that shows.
        /// </summary>
        private string DescribeRun()
        {
            return string.Format("{0} | {1:0.0}s | kills {2} | rounds done {3} | armour broken {4} | {5}",
                _phase,
                _stats.RunTime,
                _stats.TotalKills,
                _stats.CompletedRounds,
                _stats.ArmorPiecesBroken,
                RunFlowClock.IsFrozen ? "FROZEN" : "running");
        }

        private string DescribeRound()
        {
            Spawn.EnemySpawnDirector spawn = ResolveSpawnDirector();

            return string.Format(
                "R{0} | killed {1}/{2} ({3:0.0}%) | pool left {4} | alive {5}{6}{7}",
                _round,
                _killsThisRound,
                _poolTotal,
                RoundProgress01 * 100f,
                spawn != null ? spawn.RemainingInPool : 0,
                spawn != null ? spawn.ActiveCount : 0,
                _poolExhausted ? " | POOL EMPTY" : "",
                _phase == RunPhase.Countdown
                    ? string.Format(" | next in {0:0.0}s", Mathf.Max(0f, _countdownRemaining))
                    : "");
        }

        private string DescribeXp()
        {
            return string.Format("kill {0} + round {1} = {2} XP | gold earned {3} spent {4} (0 XP from gold)",
                _stats.KillXp,
                _stats.CompletedRoundXp,
                _stats.TotalXp,
                Wallet != null ? Wallet.TotalEarned : 0,
                Wallet != null ? Wallet.TotalSpent : 0);
        }

        private static string DescribeBridge()
        {
            return string.Format("{0} | round {1} | progress {2:0.0}% (kills/pool)",
                Weapons.SpecialWeaponRoundBridge.IsBound ? "BOUND" : "NOT BOUND - no auto Special Weapon",
                Weapons.SpecialWeaponRoundBridge.CurrentRound,
                Weapons.SpecialWeaponRoundBridge.RoundProgress01 * 100f);
        }

        private void DrawOffer()
        {
            if (!_shop.IsOpen)
            {
                GUILayout.Label("No offer open. Round " + _round + ", " +
                                (Wallet != null ? Wallet.Balance : 0) + " gold in the wallet.");
                return;
            }

            GUILayout.Label(string.Format("Round {0} offer - {1} gold - no reroll", _shop.Round, _shop.Gold));

            IReadOnlyList<UpgradeCardOffer> cards = _shop.Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                UpgradeCardOffer card = cards[i];

                string label = string.Format("{0}  {1}g  ({2})",
                    card.TitleWithTier, card.Cost,
                    card.Purchased ? "bought" : _shop.CanAfford(i) ? "buy" : "too expensive");

                if (GUILayout.Button(label) && !card.Purchased)
                {
                    _shop.TryPurchase(i);
                }

                GUILayout.Label("    " + card.Description);
            }

            if (GUILayout.Button("CONTINUE (skip the rest)"))
            {
                CloseUpgradeScreen();
            }
        }
    }
}
