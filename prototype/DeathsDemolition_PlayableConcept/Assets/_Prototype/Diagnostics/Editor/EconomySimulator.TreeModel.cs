using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DD.Prototype.Achievements;
using DD.Prototype.Core.Config;
using DD.Prototype.Economy;
using DD.Prototype.Profile;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Diagnostics.EditorTools
{
    public static partial class EconomySimulator
    {
        /// <summary>
        /// WP-077 B3 · the in-run tree on top of the WP-041 round model (direction §4.2 / §4.3, DEC-098).
        /// Income per round is the base simulator's crowd gold × pickup fraction (seed 1 crowd); the tree
        /// is the SHIPPED one — RunTreeState with its catalog, prerequisites, exclusions, prices, the K1
        /// hybrid draw (<see cref="RunTreeState.DrawFrontier"/>, 3 cards, Nişan guarantee), the card limit
        /// (UpgradeOfferPolicy.PurchaseLimit), Hearth outside the cards, the Purse paid at round end and
        /// the first Special pickup naming the link slots. What is a MODEL: the four bots' choices and the
        /// Special pickup roll (the round's spawn chance). Every effect is treated as live (the design
        /// target, not today's wiring). Same options → same result.
        ///
        /// Bots (the audit's restatement of §4.2, CLOUD-BALANCE-AUDIT-R1 #6):
        /// SpendMax — no pin, most expensive affordable card, full Hearth: the spend upper bound.
        /// Rush — pins the weapon capstone at the start, buys its path first, Hearth once the pin is done.
        /// Human — no pin, keeps a 30 % cushion, a random affordable card, ≤ 2 Hearth per round.
        /// Hoarder — no pin, keeps a 50 % cushion, ≤ 1 Hearth per round.
        /// </summary>
        public static class TreeModel
        {
            public enum Bot { SpendMax = 0, Rush = 1, Human = 2, Hoarder = 3 }

            public sealed class Options
            {
                public PrototypeConfig config;                       // null → PrototypeConfig.Active
                public int lastRound = 20;                           // criteria read R1–R15; the pool check runs to R20
                public float goldPickupFraction = 0.90f;             // audit #6: 90 % collection
                public int seedFrom = 1, seedTo = 20;
                public ClassicWeaponId[] weapons = { ClassicWeaponId.AssaultRifle, ClassicWeaponId.AutoSmg, ClassicWeaponId.CombatShotgun, ClassicWeaponId.Sr1 };
                public MetaRunModifiers meta = MetaRunModifiers.None; // CapstoneMasteryMet = K6 gate state
                public int offerCards = 3;
                public float humanCushion = 0.30f, hoarderCushion = 0.50f;
                public int humanHearthPerRound = 2, hoarderHearthPerRound = 1;
                public string label = "shipped";
                public int hearthMaxStacks;                          // what-if: 0 = catalog (20)
                public int lateCardLimit, lateCardFromRound = 10;    // what-if: 0 = the shipped card limit every round
            }

            [Serializable]
            public sealed class RunResult
            {
                public string weapon;
                public int seed;
                public Bot bot;
                public int income, spent, incomeLate, spentLate;     // R1–R15 and R10–R15
                public int walletR15;
                public int firstKeystone, firstCapstone;             // 0 = none by R15
                public int hearth, hearthCapRound, purseRound, specialRound;
                public int poolExhaustedRound;                       // first round ≤ lastRound with no offerable card left (0 = never)
                public float Share => income > 0 ? (float)spent / income : 0f;
                public float ShareLate => incomeLate > 0 ? (float)spentLate / incomeLate : 0f;
            }

            [Serializable]
            public sealed class BotSummary
            {
                public Bot bot;
                public int runs;
                public float shareMedian, shareLateMedian;
                public int walletMedian, walletP90;
                public int keystoneMedian, capstoneMedian, capstoneP90; // capstone: runs without one count as 16
                public float capstoneByR15;                          // share of runs with a capstone by R15
                public int hearthMedian, hearthCapRoundMedian, poolExhausted;   // cap round: runs that never cap count as 21
            }

            public sealed class Result
            {
                public Options options;
                public List<RunResult> runs = new List<RunResult>();
                public BotSummary[] bots = new BotSummary[4];
                public int[] incomeByRound = Array.Empty<int>();     // ar_1 track, for the report
                public int weaponCount;
            }

            public static Result Run(Options o)
            {
                if (o == null) throw new ArgumentNullException(nameof(o));
                PrototypeConfig config = o.config != null ? o.config : PrototypeConfig.Active;
                if (config == null) throw new InvalidOperationException("TreeModel: no PrototypeConfig.");

                Report baseReport = EconomySimulator.Run(new EconomySimulator.Options
                {
                    config = config, seed = 1u, firstRound = 1, lastRound = Math.Max(15, o.lastRound),
                    goldPickupFraction = o.goldPickupFraction, weapons = o.weapons, threatColumn = false
                });

                var result = new Result { options = o, weaponCount = baseReport.weapons.Length };
                bool live = RunTreeEffects.TreatAllAsLive;
                RunTreeEffects.TreatAllAsLive = true;
                if (o.hearthMaxStacks > 0)
                {
                    RunTreeCatalogDocument doc = JsonUtility.FromJson<RunTreeCatalogDocument>(JsonUtility.ToJson(RunTreeCatalog.Document));
                    doc.hearth.maxStacks = o.hearthMaxStacks;
                    RunTreeCatalog.TestSeam.Override(doc);
                }

                try
                {
                    foreach (WeaponTrack track in baseReport.weapons)
                    {
                        ClassicWeaponId weapon = (ClassicWeaponId)Enum.Parse(typeof(ClassicWeaponId), track.weapon);
                        if (weapon == ClassicWeaponId.AssaultRifle) result.incomeByRound = Array.ConvertAll(track.rounds, r => r.goldIncome);
                        for (int seed = o.seedFrom; seed <= o.seedTo; seed++)
                        {
                            foreach (Bot bot in (Bot[])Enum.GetValues(typeof(Bot)))
                            {
                                result.runs.Add(Simulate(o, config, track, weapon, seed, bot));
                            }
                        }
                    }
                }
                finally
                {
                    RunTreeEffects.TreatAllAsLive = live;
                    if (o.hearthMaxStacks > 0) RunTreeCatalog.TestSeam.Override(null);
                }

                foreach (Bot bot in (Bot[])Enum.GetValues(typeof(Bot))) result.bots[(int)bot] = Summarise(result.runs, bot);
                return result;
            }

            private static RunResult Simulate(Options o, PrototypeConfig config, WeaponTrack track, ClassicWeaponId weapon, int seed, Bot bot)
            {
                var random = new SeededUpgradeOfferRandom(seed * 7919 + (int)weapon * 131 + (int)bot);
                var wallet = new GoldWallet();
                wallet.InitializeForRun(1);
                var build = new WeaponModifierState();
                var tree = new RunTreeState();
                tree.Begin(1, weapon, config, o.meta);
                if (bot == Bot.Rush) tree.SetPin(tree.Graph.WeaponCapstoneIndex);

                int limit = UpgradeOfferPolicy.PurchaseLimit(config);
                var cards = new List<int>(o.offerCards + 1);
                var run = new RunResult { weapon = RunTreeCatalog.SlotFor(weapon), seed = seed, bot = bot };
                int rounds = Math.Min(o.lastRound, track.rounds.Length);

                for (int r = 1; r <= rounds; r++)
                {
                    RoundRow row = track.rounds[r - 1];

                    // The round: pickups, then the first Special (if any) names the link slots.
                    wallet.Credit(row.goldIncome, GoldSource.EnemyPickup, wallet.NextTransactionId());
                    if (run.specialRound == 0 && random.NextValue() < Mathf.Clamp01(row.expectedSpecialPickups))
                    {
                        run.specialRound = r;
                        tree.OnSpecialPickedUp(random.NextValue() < 0.5f ? "sw_arc" : "sw_gel");
                    }

                    // The round-end offer: Purse, the draw, the bot's cards, Hearth.
                    int purse = tree.OnRoundEnd(wallet);
                    int roundIncome = row.goldIncome + purse;
                    int spentBefore = wallet.Balance;
                    float floor = wallet.Balance * Cushion(o, bot);

                    if (tree.DrawFrontier(random, build, o.offerCards, cards) == 0 && run.poolExhaustedRound == 0) run.poolExhaustedRound = r;
                    tree.RecordOffer(cards, build);
                    int cardsThisRound = o.lateCardLimit > 0 && r >= o.lateCardFromRound ? o.lateCardLimit : limit;
                    for (int pick = 0; pick < cardsThisRound; pick++)
                    {
                        int card = Choose(tree, build, cards, wallet.Balance, floor, bot, random);
                        if (card < 0) break;
                        Buy(tree, build, wallet, card, config, r, run);
                        cards.Remove(card);
                    }

                    int hearthCap = HearthPerRound(o, bot, tree);
                    for (int h = 0; h < hearthCap; h++)
                    {
                        int price = tree.Graph.HearthIndex >= 0 ? tree.Cost(tree.Graph.HearthIndex, build) : int.MaxValue;
                        if (wallet.Balance - price < floor) break;
                        if (tree.TryStokeHearth(wallet, build) != RunTreeCommandResult.Done) break;
                    }

                    int spentThisRound = spentBefore - wallet.Balance;
                    if (r <= 15)
                    {
                        run.income += roundIncome;
                        run.spent += spentThisRound;
                        if (r >= 10)
                        {
                            run.incomeLate += roundIncome;
                            run.spentLate += spentThisRound;
                        }

                        if (r == 15) run.walletR15 = wallet.Balance;
                    }

                    if (run.hearthCapRound == 0 && tree.Graph.HearthIndex >= 0 && tree.HearthStacks >= tree.Graph[tree.Graph.HearthIndex].MaxTier) run.hearthCapRound = r;
                    if (run.purseRound == 0 && tree.Graph.PurseIndex >= 0 && tree.IsOwned(tree.Graph.PurseIndex)) run.purseRound = r;
                }

                run.hearth = tree.HearthStacks;
                return run;
            }

            private static float Cushion(Options o, Bot bot)
            {
                switch (bot)
                {
                    case Bot.Human: return o.humanCushion;
                    case Bot.Hoarder: return o.hoarderCushion;
                    default: return 0f;
                }
            }

            private static int HearthPerRound(Options o, Bot bot, RunTreeState tree)
            {
                switch (bot)
                {
                    case Bot.Rush: return tree.PinnedIndex >= 0 ? 0 : int.MaxValue;   // the tree's own ≤ 3 still applies
                    case Bot.Human: return o.humanHearthPerRound;
                    case Bot.Hoarder: return o.hoarderHearthPerRound;
                    default: return int.MaxValue;
                }
            }

            private static int Choose(RunTreeState tree, WeaponModifierState build, List<int> cards, int gold, float floor, Bot bot, IUpgradeOfferRandom random)
            {
                int best = -1, bestCost = 0;
                int affordable = 0;
                for (int k = 0; k < cards.Count; k++)
                {
                    int i = cards[k];
                    if (!tree.IsOfferable(i, build)) continue;              // an exclusion partner bought this round
                    int cost = tree.Cost(i, build);
                    if (gold - cost < floor) continue;
                    affordable++;
                    switch (bot)
                    {
                        case Bot.SpendMax:
                            if (best < 0 || cost > bestCost) { best = i; bestCost = cost; }
                            break;
                        case Bot.Rush:
                        {
                            int step = tree.PinnedOfferable(build);
                            if (i == step) return i;
                            if (best < 0 || cost < bestCost) { best = i; bestCost = cost; }
                            break;
                        }
                        default:
                            // Human / Hoarder: an even pick among the affordable cards (reservoir draw).
                            if (random.NextValue() * affordable < 1f) { best = i; bestCost = cost; }
                            break;
                    }
                }

                if (bot == Bot.Rush && best >= 0 && tree.PinnedIndex >= 0)
                {
                    // Rush keeps the next pinned step affordable next round: a side card only from spare gold.
                    int step = tree.PinnedOfferable(build);
                    int reserve = step >= 0 ? tree.Cost(step, build) : 0;
                    if (gold - bestCost < reserve) return -1;
                }

                return best;
            }

            private static void Buy(RunTreeState tree, WeaponModifierState build, GoldWallet wallet, int i, PrototypeConfig config, int round, RunResult run)
            {
                int cost = tree.Cost(i, build);
                if (wallet.TrySpend(cost, GoldSource.UpgradePurchase, wallet.NextTransactionId()) != GoldTransactionResult.Applied) return;
                RunTreeNode n = tree.Graph[i];
                if (n.Kind == RunTreeNodeKind.Tier) build.SetTier(n.Branch, build.GetTier(n.Branch) + 1, config);
                else tree.MarkOwned(i);

                if (n.Lane == RunTreeLane.Weapon)
                {
                    if (n.Kind == RunTreeNodeKind.BranchKeystone && run.firstKeystone == 0) run.firstKeystone = round;
                    if (n.Kind == RunTreeNodeKind.Capstone && run.firstCapstone == 0 && round <= 15) run.firstCapstone = round;
                }
            }

            private static BotSummary Summarise(List<RunResult> runs, Bot bot)
            {
                var share = new List<float>(); var late = new List<float>();
                var wallet = new List<int>(); var ks = new List<int>(); var cap = new List<int>(); var hearth = new List<int>(); var hearthCap = new List<int>();
                int withCap = 0, exhausted = 0;
                foreach (RunResult r in runs)
                {
                    if (r.bot != bot) continue;
                    share.Add(r.Share); late.Add(r.ShareLate); wallet.Add(r.walletR15); hearth.Add(r.hearth); hearthCap.Add(r.hearthCapRound > 0 ? r.hearthCapRound : 21);
                    ks.Add(r.firstKeystone > 0 ? r.firstKeystone : 16);
                    cap.Add(r.firstCapstone > 0 ? r.firstCapstone : 16);
                    if (r.firstCapstone > 0) withCap++;
                    if (r.poolExhaustedRound > 0) exhausted++;
                }

                return new BotSummary
                {
                    bot = bot, runs = share.Count,
                    shareMedian = Percentile(share, 0.5f), shareLateMedian = Percentile(late, 0.5f),
                    walletMedian = Percentile(wallet, 0.5f), walletP90 = Percentile(wallet, 0.9f),
                    keystoneMedian = Percentile(ks, 0.5f), capstoneMedian = Percentile(cap, 0.5f), capstoneP90 = Percentile(cap, 0.9f),
                    capstoneByR15 = share.Count > 0 ? (float)withCap / share.Count : 0f,
                    hearthMedian = Percentile(hearth, 0.5f), hearthCapRoundMedian = Percentile(hearthCap, 0.5f), poolExhausted = exhausted
                };
            }

            private static float Percentile(List<float> v, float p)
            {
                if (v.Count == 0) return 0f;
                v.Sort();
                return v[Mathf.Clamp(Mathf.CeilToInt(p * v.Count) - 1, 0, v.Count - 1)];
            }

            private static int Percentile(List<int> v, float p)
            {
                if (v.Count == 0) return 0;
                v.Sort();
                return v[Mathf.Clamp(Mathf.CeilToInt(p * v.Count) - 1, 0, v.Count - 1)];
            }

            // ------------------------------------------------------------------ Kül tempo (§3.4 rings, audit #5)

            [Serializable]
            public sealed class AshProfile
            {
                public string name;
                public int deathRound;                               // clears R1..d−1, dies half-way through d
                public float xpPerHour, ashPerHour;
                public float[] ringHours = new float[3];             // cumulative ring cost / Kül per hour (XP-only)
                public float ring2HoursWithOneOffs;                  // ring 1+2 less half the one-off pool (first R10s, first capstone / Link II, achievements)
                public float levelAtRing2;
            }

            public sealed class AshTempo
            {
                public int xpPerAsh;
                public int[] ringCost = new int[3];                  // cumulative Kül of ring 1, 1+2, 1+2+3
                public int oneOffPool;                               // every one-off Kül: 10 first-R10s + first capstone + first Link II + Σ achievement rarity Kül
                public AshProfile[] profiles = Array.Empty<AshProfile>();
            }

            /// <summary>
            /// The rings of direction §3.4 read off the shipped meta catalog: ring 1 = one Special leaning's
            /// first node + the passive column 1; ring 2 = the Kül weapons gated ≤ L17 + one leaning's columns
            /// 2–3 + Early Bell + passive columns 2–4; ring 3 = the rest. XP per hour from the round model
            /// (ar_1 track): clear time + <paramref name="overheadSeconds"/> per round (offer, walk-in).
            /// </summary>
            public static AshTempo Tempo(PrototypeConfig config = null, float overheadSeconds = 20f)
            {
                config = config != null ? config : PrototypeConfig.Active;
                Report baseReport = EconomySimulator.Run(new EconomySimulator.Options
                {
                    config = config, seed = 1u, lastRound = 15, goldPickupFraction = 0.90f,
                    weapons = new[] { ClassicWeaponId.AssaultRifle }, threatColumn = false
                });
                RoundRow[] rows = baseReport.weapons[0].rounds;

                var tempo = new AshTempo { xpPerAsh = Math.Max(1, config.Progression.XpPerAsh) };
                int ring1 = 0, ring2 = 0, total = 0;
                bool lean = false;
                foreach (MetaPathNode n in MetaPathCatalog.Nodes)
                {
                    total += n.AshCost;
                    switch (n.Kind)
                    {
                        case MetaNodeKind.Passive:
                            if (n.Column == 0) ring1 += n.AshCost;
                            else if (n.Column <= 3) ring2 += n.AshCost;
                            break;
                        case MetaNodeKind.SpecialLean:
                            if (n.Row == 0 && n.Column == 0 && !lean) { ring1 += n.AshCost; lean = true; }
                            else if ((n.Row == 0 && (n.Column == 1 || n.Column == 2)) || (n.Row == 2 && n.Column == 1)) ring2 += n.AshCost;
                            break;
                        case MetaNodeKind.Weapon:
                            if (n.AshCost > 0 && n.LevelGate <= 17) ring2 += n.AshCost;
                            break;
                    }
                }

                tempo.ringCost[0] = ring1;
                tempo.ringCost[1] = ring1 + ring2;
                tempo.ringCost[2] = total;
                ProgressionConfigSection prog = config.Progression;
                tempo.oneOffPool = 10 * prog.AshFirstRound10PerWeapon + prog.AshFirstCapstone + prog.AshFirstLinkII;
                foreach (AchievementDefinition a in AchievementCatalog.Definitions) tempo.oneOffPool += prog.AshForRarity((int)a.Rarity);

                var profiles = new List<AshProfile>();
                foreach (var p in new[] { ("reaches R15", 16), ("median (dies R9)", 9), ("new player (dies R6)", 6), ("direction reference 4 500 XP/h", 0) })
                {
                    float xp = 0f, seconds = 0f;
                    for (int r = 1; r < p.Item2 && r <= rows.Length; r++)
                    {
                        xp += rows[r - 1].xpRound;
                        seconds += rows[r - 1].clearSeconds + overheadSeconds;
                    }

                    if (p.Item2 >= 1 && p.Item2 <= rows.Length)
                    {
                        xp += 0.5f * rows[p.Item2 - 1].xpRound;
                        seconds += 0.5f * rows[p.Item2 - 1].clearSeconds;
                    }

                    var ap = new AshProfile { name = p.Item1, deathRound = p.Item2 };
                    ap.xpPerHour = p.Item2 == 0 ? ReferenceXpPerHour : seconds > 0f ? xp * 3600f / seconds : 0f;
                    ap.ashPerHour = ap.xpPerHour / tempo.xpPerAsh;
                    for (int k = 0; k < 3; k++) ap.ringHours[k] = ap.ashPerHour > 0f ? tempo.ringCost[k] / ap.ashPerHour : float.PositiveInfinity;
                    ap.ring2HoursWithOneOffs = ap.ashPerHour > 0f ? Mathf.Max(0f, tempo.ringCost[1] - 0.5f * tempo.oneOffPool) / ap.ashPerHour : float.PositiveInfinity;
                    ap.levelAtRing2 = LevelAfter(config, ap.xpPerHour * ap.ringHours[1]);
                    profiles.Add(ap);
                }

                tempo.profiles = profiles.ToArray();
                return tempo;
            }

            /// <summary>WP-077 §4.1 / audit #14: the direction's median pace (a median-R9 player).</summary>
            public const float ReferenceXpPerHour = 4500f;

            private static float LevelAfter(PrototypeConfig config, float xp)
            {
                int level = 1;
                while (level < 50 && xp >= config.LevelRequirement(level))
                {
                    xp -= config.LevelRequirement(level);
                    level++;
                }

                return level;
            }

            // ------------------------------------------------------------------ output

            public static string ToCsv(Result r)
            {
                var sb = new StringBuilder("weapon,seed,bot,income,spent,incomeLate,spentLate,walletR15,firstKeystone,firstCapstone,hearth,purseRound,specialRound,poolExhaustedRound\n");
                foreach (RunResult x in r.runs)
                {
                    sb.Append(x.weapon).Append(',').Append(x.seed).Append(',').Append(x.bot).Append(',').Append(x.income).Append(',').Append(x.spent).Append(',')
                      .Append(x.incomeLate).Append(',').Append(x.spentLate).Append(',').Append(x.walletR15).Append(',').Append(x.firstKeystone).Append(',')
                      .Append(x.firstCapstone).Append(',').Append(x.hearth).Append(',').Append(x.purseRound).Append(',').Append(x.specialRound).Append(',')
                      .Append(x.poolExhaustedRound).Append('\n');
                }

                return sb.ToString();
            }

            public static string ToMarkdown(Result r, AshTempo tempo)
            {
                var ic = CultureInfo.InvariantCulture;
                var sb = new StringBuilder();
                Options o = r.options;
                sb.AppendLine("# WP-077 B3 · tree model [" + o.label + "] — seeds " + o.seedFrom + "–" + o.seedTo + " × " + r.weaponCount + " weapons · pickup " + o.goldPickupFraction.ToString("0.00", ic)
                              + " · capstone mastery met " + o.meta.CapstoneMasteryMet + " · Hearth step " + (o.meta.HearthCostStepOverride >= 0 ? o.meta.HearthCostStepOverride.ToString(ic) : "catalog")
                              + (o.hearthMaxStacks > 0 ? " · Hearth cap " + o.hearthMaxStacks : string.Empty) + (o.lateCardLimit > 0 ? " · " + o.lateCardLimit + " cards from R" + o.lateCardFromRound : string.Empty));
                sb.AppendLine();
                sb.AppendLine("| bot | runs | share R1–15 | share R10–15 | wallet R15 med / p90 | 1st keystone | capstone med / p90 | capstone by R15 | Hearth (R20) / capped at | pool empty |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
                foreach (BotSummary b in r.bots)
                {
                    sb.Append("| ").Append(b.bot).Append(" | ").Append(b.runs).Append(" | ")
                      .Append((b.shareMedian * 100f).ToString("0", ic)).Append("% | ").Append((b.shareLateMedian * 100f).ToString("0", ic)).Append("% | ")
                      .Append(b.walletMedian).Append(" / ").Append(b.walletP90).Append(" | R").Append(b.keystoneMedian).Append(" | ")
                      .Append(b.capstoneMedian > 15 ? "—" : "R" + b.capstoneMedian).Append(" / ").Append(b.capstoneP90 > 15 ? "—" : "R" + b.capstoneP90).Append(" | ")
                      .Append((b.capstoneByR15 * 100f).ToString("0", ic)).Append("% | ").Append(b.hearthMedian).Append(" / ").Append(b.hearthCapRoundMedian > 20 ? "—" : "R" + b.hearthCapRoundMedian).Append(" | ").Append(b.poolExhausted).AppendLine(" |");
                }

                if (tempo != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("Kül tempo (XP / " + tempo.xpPerAsh + "): ring 1 " + tempo.ringCost[0] + " · 1+2 " + tempo.ringCost[1] + " · all " + tempo.ringCost[2] + " Kül · one-off pool " + tempo.oneOffPool + " Kül. Round-model paces are the WP-045 bot-calibrated clear times + 20 s per round.");
                    sb.AppendLine();
                    sb.AppendLine("| profile | XP/h | Kül/h | ring 1 h | ring 1+2 h (XP-only / − ½ one-offs) | all h | level at ring 2 |");
                    sb.AppendLine("|---|---|---|---|---|---|---|");
                    foreach (AshProfile p in tempo.profiles)
                    {
                        sb.Append("| ").Append(p.name).Append(" | ").Append(p.xpPerHour.ToString("0", ic)).Append(" | ").Append(p.ashPerHour.ToString("0", ic)).Append(" | ")
                          .Append(p.ringHours[0].ToString("0.00", ic)).Append(" | ").Append(p.ringHours[1].ToString("0.0", ic)).Append(" / ").Append(p.ring2HoursWithOneOffs.ToString("0.0", ic)).Append(" | ").Append(p.ringHours[2].ToString("0", ic)).Append(" | L")
                          .Append(p.levelAtRing2.ToString("0", ic)).AppendLine(" |");
                    }
                }

                return sb.ToString();
            }
        }
    }
}
