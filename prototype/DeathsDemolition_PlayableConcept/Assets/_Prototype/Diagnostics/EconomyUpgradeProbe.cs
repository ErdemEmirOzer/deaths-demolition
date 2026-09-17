#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.Boot;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Enemies;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Spawn;
using DD.Prototype.UI;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DD.Prototype.Diagnostics
{
    /// <summary>
    /// WP-008 · test-only economy / upgrade-rules probe for the real Player.
    ///
    /// Drives one real run through the public flow API and records the WP-008 contract
    /// end to end, in the Player rather than the Editor:
    ///
    ///   kill -> immutable spawn reward snapshot (base + version + spawn armour, unchanged
    ///   by breaking the plates) -> one physical packet carrying exactly that total ->
    ///   pickup / round-end sweep -> wallet -> three-card offer (distinct, valid,
    ///   unaffordable cards shown) -> one paid reroll (one or two cards change, second
    ///   reroll refused) -> one purchase (offer locked, second purchase refused) ->
    ///   reconnect keeps the offer and its rights -> CONTINUE -> next round keeps gold and
    ///   build -> death -> Results summary with Gold Spent = purchases + rerolls -> new run
    ///   with zero gold and a stock build.
    ///
    /// Nothing here changes product behaviour: it calls the same public methods the
    /// screens and the F1 panel call and reads public state. It writes screenshots at the
    /// visible moments as manual-kit evidence.
    ///
    /// Compiled ONLY into development builds and the Editor (file-level #if); a release
    /// Player contains none of it, and without <c>-ddEconomyProbe</c> a development Player
    /// allocates nothing either.
    ///
    /// Command line:
    ///   -ddEconomyProbe &lt;outDir&gt;   enable; write stage JSON files + screenshots into outDir, then quit
    ///   -ddProfilePath &lt;file&gt;      redirect the profile file (keeps the real profile untouched)
    /// </summary>
    public static class EconomyUpgradeProbe
    {
        private const string OutFlag = "-ddEconomyProbe";
        private const string ProfileFlag = "-ddProfilePath";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RedirectProfile()
        {
            if (string.IsNullOrEmpty(Arg(OutFlag)))
            {
                return;
            }

            string profilePath = Arg(ProfileFlag);
            if (!string.IsNullOrEmpty(profilePath))
            {
                ProfileStore.SetPathOverride(profilePath);
                Debug.Log("[EconomyProbe] profile path override -> " + profilePath);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string outDir = Arg(OutFlag);
            if (string.IsNullOrEmpty(outDir))
            {
                return;
            }

            var carrier = new GameObject("EconomyUpgradeProbe");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            carrier.AddComponent<Driver>().Configure(outDir);
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        [Serializable]
        public sealed class Stage
        {
            public string stage;
            public string utc;
            public string scene;
            public bool pass;
            public string note;
            public string kit;

            public int runId;
            public int runRound;
            public string runPhase;
            public float timeScale;

            public int walletRunId;
            public bool walletOpen;
            public int balance;
            public int totalEarned;
            public int totalSpent;
            public int spentOnUpgrades;
            public int spentOnRerolls;
            public int appliedTransactions;

            public int packetsSpawned;
            public int packetsCollected;
            public int ledgerGranted;
            public int ledgerRejected;
            public int lastRewardBase;
            public int lastRewardVersion;
            public int lastRewardArmor;
            public int lastRewardTotal;

            public bool shopOpen;
            public string shopStatus;
            public int shopSession;
            public int shopRevision;
            public string purchaseEntitlement;
            public string rerollEntitlement;
            public string rerollAvailability;
            public int rerollCost;
            public int cardCount;
            public string cards;
            public int purchasesThisRound;
            public int rerollsThisRound;

            public string build;
            public int purchaseCount;
            public int statsGoldSpent;
            public int statsRerollsUsed;

            public int profileXp;
            public string screenshot;
            public string detail;
        }

        private sealed class Driver : MonoBehaviour
        {
            private const float SceneTimeout = 30f;
            private string _outDir;
            private readonly List<Stage> _stages = new List<Stage>();
            private int _credits;
            private int _creditAmount;
            private GoldWallet _boundWallet;

            internal void Configure(string outDir)
            {
                _outDir = outDir;
            }

            private IEnumerator Start()
            {
                Directory.CreateDirectory(_outDir);

                yield return WaitForScene(PrototypeScenes.MainMenu);
                yield return null;
                int xpBefore = ProfileService.Current.TotalXpEarned;

                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection);
                PrototypeUiFlow.SelectedWeapon = ClassicWeaponId.AssaultRifle;
                PrototypeUiFlow.StartRun();
                yield return WaitForScene(PrototypeScenes.Run);
                yield return WaitUntil(() => RunDirector.Instance != null && EnemySpawnDirector.Instance != null && GoldService.Instance != null &&
                                             PlayerRegistry.Primary != null && WeaponRegistry.ActiveController != null &&
                                             RunDirector.Instance.Phase == RunPhase.Fighting, "run did not reach Fighting");

                RunDirector run = RunDirector.Instance;
                EnemySpawnDirector dir = EnemySpawnDirector.Instance;
                GoldService gold = GoldService.Instance;
                PlayerRig rig = PlayerRegistry.Primary;
                WeaponController weapon = WeaponRegistry.ActiveController;
                // WP-049C (DEC-087): the shipped limit is 2 cards per round; this WP-008 probe documents the
                // limit-1 contract (lock after one purchase), so it runs on a clone with PurchasesPerRound = 1;
                // the shipped config is swapped back right after the last shop stage.
                PrototypeConfig shippedConfig = PrototypeConfig.Active;
                PrototypeConfig config = UnityEngine.Object.Instantiate(shippedConfig);
                config.name = shippedConfig.name + " (probe, PurchasesPerRound=1)";
                config.Economy.PurchasesPerRound = 1;
                PrototypeConfig.SetActive(config);
                int baseReward = GoldRewardCalculator.BaseReward(config);
                int fullArmorBonus = GoldRewardCalculator.ArmorBonus(new SpawnArmorComposition(true, true, true), config);
                rig.Health.DiagnosisInvulnerable = true;
                BindWallet(gold.Wallet);

                SetSpawning(dir, false);
                dir.ClearAllEnemies();
                EnemyFactory.ClearAll();
                gold.ClearOrbs(false);
                yield return null;

                // ---------------------------------------------------------------- 01: spawn snapshot, armour broken, one packet with the snapshot total
                Vector3 killPos = rig.transform.position + new Vector3(12f, 0f, 0f);
                ZombieEnemy z = EnemyFactory.Spawn(EnemyTier.Standard, run.Round, killPos, Quaternion.identity);
                int fitted = z != null ? z.FitArmorComposition(true, true, true) : 0;
                EnemyRewardSnapshot snapAtSpawn = z != null ? z.Reward : default;
                int broken = z != null ? z.BreakAllArmor() : 0;
                EnemyRewardSnapshot snapAfterBreak = z != null ? z.Reward : default;
                int grantedBefore = gold.Ledger.Granted;
                if (z != null) z.Kill();
                yield return null;
                GoldOrb packet = OnlySpawnedPacket();
                int expectedTotal = baseReward + fullArmorBonus;
                Stage s1 = Capture("01-spawn-snapshot-armor-break-packet", run, gold, "A");
                s1.pass = fitted == 3 && broken == 3 && snapAtSpawn.IsValid && snapAtSpawn.Total == expectedTotal && snapAtSpawn.Base == baseReward &&
                          snapAtSpawn.VersionBonus == 0 && snapAtSpawn.ArmorBonus == fullArmorBonus && snapAfterBreak.Total == snapAtSpawn.Total &&
                          snapAfterBreak.ArmorBonus == snapAtSpawn.ArmorBonus && gold.Ledger.Granted == grantedBefore + 1 && packet != null &&
                          packet.Amount == expectedTotal && gold.LastRewardValue.Total == expectedTotal && gold.LastRewardValue.ArmorContribution == fullArmorBonus;
                s1.detail = "fitted=" + fitted + " broken=" + broken + " snapshot " + snapAtSpawn.Base + "+" + snapAtSpawn.VersionBonus + "+" + snapAtSpawn.ArmorBonus +
                            "=" + snapAtSpawn.Total + " afterBreak=" + snapAfterBreak.Total + " packet=" + (packet != null ? packet.Amount : -1) + " expected=" + expectedTotal;
                s1.note = "H+B+L Standard: reward frozen at spawn, all plates broken, the one packet still carries base + full armour bonus";
                yield return Shot(s1, "01-packet.png");
                Write(s1);

                // ---------------------------------------------------------------- 02: version bonus per family (rolled armour included in the snapshot)
                bool versionsOk = true;
                var versionDetail = new System.Text.StringBuilder(128);
                EnemyTier[] tiers = { EnemyTier.Standard, EnemyTier.Special, EnemyTier.Elite, EnemyTier.Ultra };
                for (int i = 0; i < tiers.Length; i++)
                {
                    ZombieEnemy v = EnemyFactory.Spawn(tiers[i], run.Round, rig.transform.position + new Vector3(-10f - i * 2f, 0f, 3f), Quaternion.identity);
                    if (v == null) { versionsOk = false; versionDetail.Append(tiers[i]).Append("=nospawn "); continue; }
                    EnemyRewardSnapshot vs = v.Reward;
                    int expectedVersion = GoldRewardCalculator.VersionBonus(tiers[i], config);
                    int expectedArmor = GoldRewardCalculator.ArmorBonus(vs.Armor, config);
                    bool ok = vs.IsValid && vs.Tier == tiers[i] && vs.Base == baseReward && vs.VersionBonus == expectedVersion && vs.ArmorBonus == expectedArmor &&
                              vs.Total == baseReward + expectedVersion + expectedArmor && vs.Total == config.GoldReward(tiers[i]) + expectedArmor;
                    versionsOk &= ok;
                    versionDetail.Append(tiers[i]).Append('=').Append(vs.Base).Append('+').Append(vs.VersionBonus).Append('+').Append(vs.ArmorBonus)
                                 .Append('(').Append(vs.Armor.Describe()).Append(")=").Append(vs.Total).Append(ok ? " " : "!! ");
                    v.Remove();
                }

                Stage s2 = Capture("02-version-bonus-breakdown", run, gold, "A");
                s2.pass = versionsOk;
                s2.detail = versionDetail.ToString().TrimEnd();
                s2.note = "Standard/Special/Elite/Ultra: base = Standard value, version bonus = family value minus base, armour = rolled plates at spawn";
                Write(s2);

                // ---------------------------------------------------------------- 03: magnet pickup credits exactly the snapshot total once
                ResetCredits();
                Vector3 near = packet.transform.position + new Vector3(-(gold.MagnetRadius - 0.8f), 0f, 0f); near.y = rig.transform.position.y;
                rig.Teleport(near, Quaternion.identity);
                float t0 = Time.realtimeSinceStartup;
                yield return WaitUntil(() => _credits > 0 || Time.realtimeSinceStartup - t0 > 12f, "magnet credit");
                for (int i = 0; i < 30; i++) yield return null;
                Stage s3 = Capture("03-pickup-credits-snapshot-total", run, gold, "B");
                s3.pass = _credits == 1 && _creditAmount == expectedTotal && gold.Wallet.Balance == expectedTotal && gold.Wallet.EarnedByPickup == expectedTotal;
                s3.detail = "credits=" + _credits + " amount=" + _creditAmount + " balance=" + gold.Wallet.Balance;
                s3.note = "one credit, exactly the snapshot total";
                Write(s3);
                EnemyFactory.ClearAll();
                gold.ClearOrbs(true);
                yield return null;

                // ---------------------------------------------------------------- 04: complete the round -> sweep -> offer of three distinct valid cards
                SetSpawning(dir, true);
                run.BeginRound(1);
                yield return null;
                int cleared = 0;
                Action<int> onCleared = r => cleared++;
                RunEvents.RoundCompleted += onCleared;
                t0 = Time.realtimeSinceStartup;
                while (cleared == 0 && Time.realtimeSinceStartup - t0 < 90f)
                {
                    KillAllLiving();
                    yield return null;
                }

                RunEvents.RoundCompleted -= onCleared;
                yield return WaitUntil(() => run.Phase == RunPhase.Upgrading, "upgrade screen");
                for (int i = 0; i < 40; i++) yield return null;
                RoundUpgradeShop shop = run.Shop;
                bool distinct = shop.Cards.Count == UpgradeOfferPolicy.CardsPerOffer;
                for (int i = 0; i < shop.Cards.Count && distinct; i++)
                {
                    distinct &= shop.Cards[i].IsValid && shop.Eligibility(shop.Cards[i].Branch) == UpgradeEligibility.Eligible;
                    for (int j = i + 1; j < shop.Cards.Count; j++) distinct &= shop.Cards[i].Branch != shop.Cards[j].Branch;
                }

                int unaffordable = 0;
                for (int i = 0; i < shop.Cards.Count; i++) if (!shop.CanAfford(i)) unaffordable++;
                Stage s4 = Capture("04-offer-three-distinct-valid", run, gold, "C");
                s4.pass = cleared == 1 && shop.IsOpen && shop.Status == UpgradeOfferStatus.Ready && distinct && shop.Revision == 1 &&
                          shop.PurchaseEntitlement == UpgradeEntitlement.Available && shop.RerollEntitlement == UpgradeEntitlement.Available &&
                          Time.timeScale == 0f && shop.Gold == gold.Wallet.Balance;
                s4.detail = "cards=" + s4.cards + " unaffordableShown=" + unaffordable + " sweep=" + gold.LastAutoCollect.Packets + "/" + gold.LastAutoCollect.Amount + "g";
                s4.note = "round complete: three distinct eligible cards on a frozen world; unaffordable cards stay visible (buy disabled)";
                yield return Shot(s4, "04-offer.png");
                Write(s4);

                // ---------------------------------------------------------------- 05: one paid reroll; second refused
                int rerollCost = shop.RerollCost;
                if (gold.Wallet.Balance < rerollCost)
                {
                    gold.Wallet.Credit(rerollCost - gold.Wallet.Balance, rig.transform.position);   // untagged top-up (probe seam)
                }

                var before = new List<UpgradeBranch>();
                for (int i = 0; i < shop.Cards.Count; i++) before.Add(shop.Cards[i].Branch);
                int balBeforeReroll = gold.Wallet.Balance;
                int sessionBefore = shop.SessionId;
                int revisionBefore = shop.Revision;
                RerollAvailability availBefore = shop.RerollAvailability;
                bool rerolled = shop.TryReroll(shop.SessionId, shop.Revision);
                yield return null;
                int changed = 0;
                bool distinctAfter = shop.Cards.Count == UpgradeOfferPolicy.CardsPerOffer;
                for (int i = 0; i < shop.Cards.Count; i++)
                {
                    if (i >= before.Count || shop.Cards[i].Branch != before[i]) changed++;
                    for (int j = i + 1; j < shop.Cards.Count; j++) distinctAfter &= shop.Cards[i].Branch != shop.Cards[j].Branch;
                }

                int balAfterReroll = gold.Wallet.Balance;
                bool second = shop.TryReroll(shop.SessionId, shop.Revision);
                UpgradeRerollResult secondResult = shop.LastRerollResult;
                Stage s5 = Capture("05-one-paid-reroll", run, gold, "D");
                s5.pass = availBefore == RerollAvailability.Available && rerolled && changed >= 1 && changed <= 2 && distinctAfter &&
                          balAfterReroll == balBeforeReroll - rerollCost && shop.Revision == revisionBefore + 1 && shop.SessionId == sessionBefore &&
                          shop.RerollEntitlement == UpgradeEntitlement.Used && !second && secondResult == UpgradeRerollResult.RerollUsed &&
                          gold.Wallet.Balance == balAfterReroll && gold.Wallet.SpentOnRerolls == rerollCost && shop.RerollAvailability == RerollAvailability.Used;
                s5.detail = "cost=" + rerollCost + " changed=" + changed + " balance " + balBeforeReroll + "->" + balAfterReroll + " second=" + secondResult +
                            " replacedSlots=" + string.Join(",", shop.LastRerollReplacedSlots) + " cards=" + s5.cards;
                s5.note = "reroll once: one or two cards replaced, three distinct, cost taken once; second reroll refused, nothing spent";
                yield return Shot(s5, "05-rerolled.png");
                Write(s5);

                // ---------------------------------------------------------------- 06: one purchase locks the offer; second purchase refused
                int cardIndex = -1;
                int cheapest = int.MaxValue;
                for (int i = 0; i < shop.Cards.Count; i++) { if (shop.Cards[i].IsValid && shop.Cards[i].Cost < cheapest) { cheapest = shop.Cards[i].Cost; cardIndex = i; } }
                UpgradeCardOffer card = cardIndex >= 0 ? shop.Cards[cardIndex] : default;
                if (cardIndex >= 0 && gold.Wallet.Balance < card.Cost)
                {
                    gold.Wallet.Credit(card.Cost - gold.Wallet.Balance, rig.transform.position);
                }

                int balBefore = gold.Wallet.Balance;
                int tierBefore = weapon.Modifiers.GetTier(card.Branch);
                int revBeforeBuy = shop.Revision;
                bool bought = cardIndex >= 0 && shop.TryPurchase(cardIndex, shop.SessionId, shop.Revision);
                yield return null;
                int other = (cardIndex + 1) % Mathf.Max(1, shop.Cards.Count);
                int balAfterBuy = gold.Wallet.Balance;
                bool secondBuy = shop.TryPurchase(other, shop.SessionId, shop.Revision);
                UpgradePurchaseResult secondBuyResult = shop.LastResult;
                bool staleRevisionBuy = shop.TryPurchase(other, shop.SessionId, revBeforeBuy);
                UpgradePurchaseResult staleResult = shop.LastResult;
                bool rerollAfterBuy = shop.TryReroll(shop.SessionId, shop.Revision);
                Stage s6 = Capture("06-one-purchase-locks-offer", run, gold, "E");
                s6.pass = bought && balAfterBuy == balBefore - card.Cost && weapon.Modifiers.GetTier(card.Branch) == tierBefore + 1 && shop.IsLocked &&
                          shop.PurchaseEntitlement == UpgradeEntitlement.Used && !secondBuy && secondBuyResult == UpgradePurchaseResult.PurchaseLimitReached &&
                          !staleRevisionBuy && staleResult == UpgradePurchaseResult.StaleRevision && !rerollAfterBuy &&
                          shop.LastRerollResult == UpgradeRerollResult.OfferLocked && gold.Wallet.Balance == balAfterBuy &&
                          shop.RerollAvailability == RerollAvailability.LockedByPurchase && run.Build.PurchaseCount == 1 &&
                          gold.Wallet.SpentOnUpgrades == card.Cost && gold.Wallet.TotalSpent == card.Cost + rerollCost;
                s6.detail = "card=" + card.Branch + " cost=" + card.Cost + " balance " + balBefore + "->" + balAfterBuy + " second=" + secondBuyResult +
                            " stale=" + staleResult + " rerollAfterBuy=" + shop.LastRerollResult + " build=" + run.Build.DescribeBuild();
                s6.note = "one purchase: gold down once, tier up once, offer locked; second purchase, stale-revision purchase and reroll all refused with nothing spent";
                yield return Shot(s6, "06-locked.png");
                Write(s6);

                // ---------------------------------------------------------------- 07: reconnect (same round reopen) keeps the offer and its rights
                int sessionLocked = shop.SessionId; int revLocked = shop.Revision; int purchasesLocked = shop.PurchasesThisRound;
                string cardsLocked = DescribeCards(shop);
                shop.Open(run.Round, config, gold.Wallet, null, run.RunId);
                yield return null;
                bool buyAfterReopen = shop.TryPurchase(other, shop.SessionId, shop.Revision);
                Stage s7 = Capture("07-reopen-keeps-rights", run, gold, "F");
                s7.pass = shop.IsOpen && shop.SessionId == sessionLocked && shop.Revision == revLocked && shop.PurchasesThisRound == purchasesLocked &&
                          shop.IsLocked && DescribeCards(shop) == cardsLocked && !buyAfterReopen && shop.LastResult == UpgradePurchaseResult.PurchaseLimitReached &&
                          gold.Wallet.Balance == balAfterBuy && shop.RerollEntitlement == UpgradeEntitlement.Used;   // used in 05, stays Used (not re-locked, never re-granted)
                s7.detail = "session " + sessionLocked + "->" + shop.SessionId + " revision " + revLocked + "->" + shop.Revision + " cards=" + DescribeCards(shop);
                s7.note = "reopening the same round's screen restores the same offer, still locked; no free offer, no new right";
                Write(s7);

                // ---------------------------------------------------------------- 08: CONTINUE -> next round keeps gold and build
                int balR1 = gold.Wallet.Balance; string buildR1 = run.Build.DescribeBuild(); int spentR1 = gold.Wallet.TotalSpent;
                run.CloseUpgradeScreen();
                yield return WaitUntil(() => run.Phase == RunPhase.Fighting && run.Round == 2, "round 2");
                for (int i = 0; i < 10; i++) yield return null;
                Stage s8 = Capture("08-continue-next-round", run, gold, "G");
                s8.pass = run.Round == 2 && gold.Wallet.Balance == balR1 && run.Build.DescribeBuild() == buildR1 && gold.Wallet.TotalSpent == spentR1 &&
                          !RunFlowClock.IsFrozen && !shop.IsOpen;
                s8.detail = "balance=" + gold.Wallet.Balance + " build=" + run.Build.DescribeBuild() + " totalSpent=" + gold.Wallet.TotalSpent;
                s8.note = "CONTINUE: round 2 running; gold, build and the spend ledger carried over";
                Write(s8);

                // ---------------------------------------------------------------- 09: death -> summary Gold Spent = purchase + reroll
                SetSpawning(dir, false);
                dir.ClearAllEnemies();
                yield return null;
                RunSummary summary = default; int ended = 0;
                Action<RunSummary> onEnded = s => { summary = s; ended++; };
                RunEvents.RunEnded += onEnded;
                int earnedAtDeath = gold.Wallet.TotalEarned;
                PrototypeConfig.SetActive(shippedConfig);   // WP-049C: shop stages done → shipped config back
                UnityEngine.Object.Destroy(config);
                rig.Health.DiagnosisInvulnerable = false;
                rig.Health.ApplyDamage(rig.Health.Max * 10f, rig.transform.position + Vector3.forward, "wp-008-probe");
                yield return WaitUntil(() => run.Phase == RunPhase.Dead, "Dead");
                RunEvents.RunEnded -= onEnded;
                Stage s9 = Capture("09-death-summary-gold-spent", run, gold, "H");
                s9.pass = ended == 1 && summary.GoldSpent == card.Cost + rerollCost && summary.GoldSpentOnUpgrades == card.Cost &&
                          summary.GoldSpentOnRerolls == rerollCost && summary.UpgradesPurchased == 1 && summary.RerollsUsed == 1 &&
                          summary.GoldEarned == earnedAtDeath && summary.GoldUnspent == balR1 && summary.BuildDescription == buildR1 &&
                          gold.Wallet.Balance == 0 && !gold.Wallet.IsOpen && !run.Build.IsRunLive;
                s9.detail = "summary goldSpent=" + summary.GoldSpent + " (upgrades " + summary.GoldSpentOnUpgrades + " + rerolls " + summary.GoldSpentOnRerolls +
                            ") purchases=" + summary.UpgradesPurchased + " rerolls=" + summary.RerollsUsed + " unspent=" + summary.GoldUnspent + " build=" + summary.BuildDescription;
                s9.note = "death: the summary's Gold Spent is purchases + rerolls; wallet and build wiped";
                Write(s9);

                yield return WaitForScene(PrototypeScenes.Results);
                for (int i = 0; i < 30; i++) yield return null;
                Stage s10 = Capture("10-results-screen", run, gold, "H");
                s10.pass = SceneManager.GetActiveScene().name == PrototypeScenes.Results;
                s10.note = "Results screen showing the run economy lines (Gold Spent = purchases + rerolls)";
                yield return Shot(s10, "10-results.png");
                Write(s10);

                // ---------------------------------------------------------------- 11: new run: zero gold, stock build, fresh rights
                int xpAfter = ProfileService.Current.TotalXpEarned;
                PrototypeUiFlow.GoToMainMenu();
                yield return WaitForScene(PrototypeScenes.MainMenu);
                PrototypeUiFlow.GoToSelection();
                yield return WaitForScene(PrototypeScenes.Selection);
                PrototypeUiFlow.StartRun();
                yield return WaitForScene(PrototypeScenes.Run);
                yield return WaitUntil(() => RunDirector.Instance != null && GoldService.Instance != null && GoldService.Instance != gold &&
                                             WeaponRegistry.ActiveController != null && RunDirector.Instance.Phase == RunPhase.Fighting, "second run Fighting");
                RunDirector run2 = RunDirector.Instance; GoldService gold2 = GoldService.Instance; WeaponController weapon2 = WeaponRegistry.ActiveController;
                for (int i = 0; i < 10; i++) yield return null;
                Stage s11 = Capture("11-new-run-clean", run2, gold2, "I");
                s11.pass = run2.Round == 1 && gold2.Wallet.IsOpen && gold2.Wallet.Balance == 0 && gold2.Wallet.TotalSpent == 0 && weapon2.Modifiers.TotalTiers == 0 &&
                           run2.Build.DescribeBuild() == "Stock" && run2.Build.PurchaseCount == 0 && !run2.Shop.IsOpen && run2.Shop.PurchasesThisRound == 0 &&
                           run2.RunId > run.RunId && xpAfter == xpBefore + summary.TotalXpEarned;
                s11.detail = "run2Id=" + run2.RunId + " balance=" + gold2.Wallet.Balance + " build=" + run2.Build.DescribeBuild() + " xp " + xpBefore + "->" + xpAfter +
                             " (+" + summary.TotalXpEarned + " from the summary; gold spent/earned contributed 0)";
                s11.note = "second run: zero gold, stock build, no offer; profile XP moved only by the summary";
                Write(s11);

                int passCount = 0;
                for (int i = 0; i < _stages.Count; i++) if (_stages[i].pass) passCount++;
                File.WriteAllText(Path.Combine(_outDir, "summary.txt"),
                    "EconomyUpgradeProbe " + DateTime.UtcNow.ToString("o") + " stages=" + _stages.Count + " pass=" + passCount + "\n" +
                    string.Join("\n", StageLines()) + "\n");
                Debug.Log("[EconomyProbe] DONE stages=" + _stages.Count + " pass=" + passCount);

                PrototypeUiFlow.AbortRunToMainMenu();
                yield return new WaitForSecondsRealtime(1f);
                PrototypeUiFlow.Quit();
            }

            private IEnumerable<string> StageLines()
            {
                for (int i = 0; i < _stages.Count; i++)
                {
                    yield return (_stages[i].pass ? "PASS " : "FAIL ") + _stages[i].stage + " [kit " + _stages[i].kit + "] | " + _stages[i].note + " | " + _stages[i].detail;
                }
            }

            private static string DescribeCards(RoundUpgradeShop shop)
            {
                var sb = new System.Text.StringBuilder(64);
                for (int i = 0; i < shop.Cards.Count; i++)
                {
                    if (i > 0) sb.Append('|');
                    sb.Append(shop.Cards[i].Branch).Append(shop.Cards[i].Purchased ? "*" : "").Append(':').Append(shop.Cards[i].Cost);
                }

                return sb.ToString();
            }

            private static void SetSpawning(EnemySpawnDirector dir, bool on)
            {
                System.Reflection.FieldInfo f = typeof(EnemySpawnDirector).GetField("_spawningEnabled",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (f != null) f.SetValue(dir, on);
            }

            private void BindWallet(GoldWallet wallet)
            {
                if (_boundWallet != null) _boundWallet.Credited -= OnCredited;
                _boundWallet = wallet;
                if (_boundWallet != null) _boundWallet.Credited += OnCredited;
            }

            private void OnCredited(int amount, int balance, Vector3 position)
            {
                _credits++;
                _creditAmount += amount;
            }

            private void ResetCredits()
            {
                _credits = 0;
                _creditAmount = 0;
            }

            private static GoldOrb OnlySpawnedPacket()
            {
                GoldOrb[] all = UnityEngine.Object.FindObjectsOfType<GoldOrb>(false);
                GoldOrb found = null;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i].IsSpawned) found = all[i];
                }

                return found;
            }

            private static int KillAllLiving()
            {
                int n = 0;
                IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    if (live[i] != null && live[i].IsAlive) { live[i].Kill(); n++; }
                }

                return n;
            }

            private Stage Capture(string stage, RunDirector r, GoldService g, string kit)
            {
                bool hasRun = !ReferenceEquals(r, null);
                bool hasGold = !ReferenceEquals(g, null);
                GoldWallet w = hasGold ? g.Wallet : null;
                RoundUpgradeShop shop = hasRun ? r.Shop : null;

                return new Stage
                {
                    stage = stage,
                    utc = DateTime.UtcNow.ToString("o"),
                    scene = SceneManager.GetActiveScene().name,
                    kit = kit,
                    runId = hasRun ? r.RunId : 0,
                    runRound = hasRun ? r.Round : 0,
                    runPhase = hasRun ? r.Phase.ToString() : "none",
                    timeScale = Time.timeScale,
                    walletRunId = w != null ? w.RunId : 0,
                    walletOpen = w != null && w.IsOpen,
                    balance = w != null ? w.Balance : 0,
                    totalEarned = w != null ? w.TotalEarned : 0,
                    totalSpent = w != null ? w.TotalSpent : 0,
                    spentOnUpgrades = w != null ? w.SpentOnUpgrades : 0,
                    spentOnRerolls = w != null ? w.SpentOnRerolls : 0,
                    appliedTransactions = w != null ? w.AppliedTransactions : 0,
                    packetsSpawned = hasGold ? g.PacketsSpawned : 0,
                    packetsCollected = hasGold ? g.PacketsCollected : 0,
                    ledgerGranted = hasGold ? g.Ledger.Granted : 0,
                    ledgerRejected = hasGold ? g.Ledger.Rejected : 0,
                    lastRewardBase = hasGold ? g.LastRewardValue.Base : 0,
                    lastRewardVersion = hasGold ? g.LastRewardValue.VariantContribution : 0,
                    lastRewardArmor = hasGold ? g.LastRewardValue.ArmorContribution : 0,
                    lastRewardTotal = hasGold ? g.LastRewardValue.Total : 0,
                    shopOpen = shop != null && shop.IsOpen,
                    shopStatus = shop != null ? shop.Status.ToString() : "none",
                    shopSession = shop != null ? shop.SessionId : 0,
                    shopRevision = shop != null ? shop.Revision : 0,
                    purchaseEntitlement = shop != null ? shop.PurchaseEntitlement.ToString() : "none",
                    rerollEntitlement = shop != null ? shop.RerollEntitlement.ToString() : "none",
                    rerollAvailability = shop != null ? shop.RerollAvailability.ToString() : "none",
                    rerollCost = shop != null ? shop.RerollCost : 0,
                    cardCount = shop != null ? shop.Cards.Count : 0,
                    cards = shop != null ? DescribeCards(shop) : "-",
                    purchasesThisRound = shop != null ? shop.PurchasesThisRound : 0,
                    rerollsThisRound = shop != null ? shop.RerollsThisRound : 0,
                    build = hasRun ? r.Build.DescribeBuild() : "-",
                    purchaseCount = hasRun ? r.Build.PurchaseCount : 0,
                    statsGoldSpent = hasRun ? r.Stats.GoldSpent : 0,
                    statsRerollsUsed = hasRun ? r.Stats.RerollsUsed : 0,
                    profileXp = ProfileService.Current.TotalXpEarned
                };
            }

            private IEnumerator Shot(Stage s, string file)
            {
                string path = Path.Combine(_outDir, file);
                ScreenCapture.CaptureScreenshot(path);
                s.screenshot = file;
                yield return null;
                yield return null;
            }

            private void Write(Stage s)
            {
                _stages.Add(s);
                File.WriteAllText(Path.Combine(_outDir, s.stage + ".json"), JsonUtility.ToJson(s, true));
                Debug.Log("[EconomyProbe] " + (s.pass ? "PASS " : "FAIL ") + s.stage + " | " + s.note + " | " + s.detail);
            }

            private static IEnumerator WaitForScene(string name)
            {
                yield return WaitUntil(
                    () => SceneManager.GetActiveScene().name == name && SceneManager.GetActiveScene().isLoaded,
                    "scene '" + name + "' not active");
            }

            private static IEnumerator WaitUntil(Func<bool> condition, string what)
            {
                float start = Time.realtimeSinceStartup;
                while (!condition())
                {
                    if (Time.realtimeSinceStartup - start > SceneTimeout)
                    {
                        Debug.LogError("[EconomyProbe] TIMEOUT: " + what + " (scene " + SceneManager.GetActiveScene().name + ")");
                        Application.Quit(3);
                        yield break;
                    }

                    yield return null;
                }
            }
        }
    }
}
#endif
