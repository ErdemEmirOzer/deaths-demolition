using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using DD.Prototype.Economy;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.Diagnostics.EditorTools
{
    /// <summary>
    /// WP-041 (ADR-041-1) · Economy &amp; progression balance simulator v1. Editor / test tool,
    /// never a Player path. Reads the SHIPPED data (PrototypeConfig, weapon roster and its
    /// core branches, WeaponUpgradeTuning, the unlock catalog's level ladder) and produces a
    /// deterministic per-round table for R1..N: time-to-kill per tier × armour state × weapon
    /// build, gold income / spend, when each upgrade tier becomes affordable, the expected
    /// special-weapon pickups, and a "death-risk proxy". Nothing here changes a value; the
    /// report is the input to the balance proposal.
    ///
    /// What is exact: the crowd composition (tier and per-slot armour counts) comes from
    /// <see cref="SpawnMixSimulator"/> (the production spawn / armour policies, ADR-027-1);
    /// health, damage, gold, XP, card prices and modifier multipliers are the production
    /// formulas. What is a MODEL (documented per field): hit fraction, the region mix the
    /// player's shots land on, the greedy "buy the cheapest affordable core card each round"
    /// build path, the crowd-damage multipliers of pierce / chain / explosion / ricochet, and
    /// the engaged-time fraction behind the risk proxy. Same seed → byte-identical report.
    /// </summary>
    public static class EconomySimulator
    {
        public const string Schema = "dd-balance-v1";
        public const int TierCount = 4;

        public sealed class Options
        {
            public PrototypeConfig config;                       // null → PrototypeConfig.Active
            public int firstRound = 1;
            public int lastRound = 15;
            public uint seed = 1u;
            public int spawnsPerRound = 0;                       // 0 → the round's real pool (whole pools, see SpawnMixSimulator)
            public float hitFraction = 0.70f;                    // MODEL: fraction of fired shots that land
            public float goldPickupFraction = 1.00f;             // MODEL: orbs collected before OrbLifetime (magnet mode) — WP-045 calibration: bot runs collected ~everything (implied 1.00; was 0.90)
            public float minSecondsPerEnemy = 2.5f;              // MODEL: round-time floor per spawn (travel + engage) — WP-045 calibration: bot R1 30 s / 12, R2 53 s / 20 (was refill-rate only, 8 s / 12)
            public float engagedFraction = 0.30f;                // MODEL: share of round time the player is inside an enemy's attack window
            public int simultaneousAttackers = 3;                // MODEL: enemies that can reach the player at once
            public bool buyEveryRound = true;                    // MODEL: one purchase per round when affordable (DEC-071)
            public ClassicWeaponId[] weapons;                    // null → every enabled roster weapon
        }

        [Serializable]
        public sealed class TierTtk
        {
            public string tier;
            public int count;                                    // spawns of this tier this round (simulator)
            public float healthEffective;                        // HP / region multiplier + expected armour durability (this round's crowd)
            public float ttkNoArmour;                            // seconds, single target, current build
            public float ttkArmoured;                            // seconds, single target wearing every piece the tier can carry
            public float ttkExpected;                            // seconds, weighted by this round's armour mix
            public float incomingDps;                            // per enemy: scaled attack damage over its attack cycle (telegraph+active+recovery+cooldown)
        }

        [Serializable]
        public sealed class RoundRow
        {
            public int round;
            public int pool;
            public int[] tierCounts = new int[TierCount];
            public float[] armouredShare = new float[TierCount]; // share of spawns wearing ≥1 piece
            public float sustainedDps;                           // build: mag/(mag/rps+reload) × damage × pellets × hit fraction × fire-rate mult
            public float crowdDps;                               // sustainedDps × crowd multiplier (pierce/chain/explosion/ricochet model)
            public float clearSeconds;                           // Σ effective HP / crowdDps, floored by the spawn pacing
            public float pacingFloorSeconds;                     // max(pool / refill rate, pool × minSecondsPerEnemy)
            public int goldIncome;                               // Σ GoldRewardCalculator.Compute over the crowd × pickup fraction
            public int goldSpent;
            public int walletAfter;
            public string purchase;                              // "Branch tN cost" or "-"
            public string build;                                 // tiers after the round, e.g. "P1 FR2 RS1"
            public int xpRound;                                  // RoundCompletionXp(round) + kill XP of the crowd
            public int xpCumulative;
            public int profileLevelIfFirstRun;                   // level reached by a fresh profile at the end of this round (single run)
            public float riskProxy;                              // expected damage taken this round / (max health + regen over the exposure) — ≥ 1 = likely death
            public float expectedSpecialPickups;                 // SpecialWeaponSpawnChance(round) (+ second spawn)
            public TierTtk[] ttk = new TierTtk[TierCount];
        }

        [Serializable]
        public sealed class WeaponTrack
        {
            public string weapon;                                // ClassicWeaponId name
            public string rosterKey;
            public int unlockLevel;
            public string coreBranches;
            public RoundRow[] rounds = Array.Empty<RoundRow>();
            public int firstRiskRound;                           // first round with riskProxy ≥ 1 (0 = none within range)
            public int firstRoundWithAnyPurchase;
            public int firstRoundWithFullCoreTier1;              // every core branch bought at least once
        }

        [Serializable]
        public sealed class Flag
        {
            public string band;                                  // DEC / model band name
            public string severity;                              // "info" | "warn" | "out-of-band"
            public string owner;                                 // data owner: S-04 (economy/unlock) | S-02 (weapons) | S-03 (tiers/armour)
            public string text;
        }

        [Serializable]
        public sealed class Report
        {
            public string schema = Schema;
            public string utc;
            public uint seed;
            public int firstRound, lastRound;
            public float hitFraction, goldPickupFraction, engagedFraction, minSecondsPerEnemy;
            public int simultaneousAttackers;
            public string[] tierNames = { "Standard", "Special", "Elite", "Ultra" };
            public int[] introductionRound = new int[TierCount];
            public int[] openingCardCost = new int[10];          // first-tier price per branch after the DEC-070 15..45 clamp
            public int[] rawOpeningCardCost = new int[10];       // BaseCost before the clamp
            public int[] levelRequirement = new int[10];         // XP to leave level 1..10
            public int[] xpToReachLevel = new int[10];           // cumulative XP to REACH level 2..11 (index = level-1)
            public WeaponTrack[] weapons = Array.Empty<WeaponTrack>();
            public Flag[] flags = Array.Empty<Flag>();
            public string[] notes = Array.Empty<string>();
        }

        // ------------------------------------------------------------------ run

        public static Report Run(Options o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            PrototypeConfig config = o.config != null ? o.config : PrototypeConfig.Active;
            if (config == null) throw new InvalidOperationException("EconomySimulator: no PrototypeConfig.");
            WeaponUpgradeTuning tuning = WeaponUpgradeTuning.Active;

            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                seed = o.seed, firstRound = Math.Max(1, o.firstRound), lastRound = Math.Max(o.firstRound, o.lastRound),
                hitFraction = o.hitFraction, goldPickupFraction = o.goldPickupFraction, engagedFraction = o.engagedFraction, minSecondsPerEnemy = o.minSecondsPerEnemy,
                simultaneousAttackers = o.simultaneousAttackers
            };

            for (int t = 0; t < TierCount; t++)
            {
                EnemyTierSettings ts = config.Enemies.Find((EnemyTier)t);
                report.introductionRound[t] = ts != null ? ts.IntroductionRound : 1;
            }

            var branches = (UpgradeBranch[])Enum.GetValues(typeof(UpgradeBranch));
            for (int i = 0; i < branches.Length && i < 10; i++)
            {
                UpgradeBranchSettings b = config.Economy.Find(branches[i]);
                if (b == null) continue;
                report.rawOpeningCardCost[i] = b.CostForTier(0);
                report.openingCardCost[i] = ClampOpening(b.CostForTier(0), config.Economy);
            }

            int cumulative = 0;
            for (int l = 1; l <= 10; l++)
            {
                report.levelRequirement[l - 1] = config.LevelRequirement(l);
                cumulative += config.LevelRequirement(l);
                report.xpToReachLevel[l - 1] = cumulative;
            }

            // Crowd composition per round: the production policies (ADR-027-1).
            var mix = SpawnMixSimulator.Run(new SpawnMixSimulator.Options
            {
                config = config, firstRound = report.firstRound, lastRound = report.lastRound,
                spawnsPerRound = Math.Max(1, o.spawnsPerRound), seed = o.seed
            });

            IReadOnlyList<ClassicWeaponDefinition> roster = WeaponRoster.Enabled;
            var tracks = new List<WeaponTrack>();
            foreach (ClassicWeaponDefinition d in roster)
            {
                if (d == null || !d.HasRuntimeId) continue;
                if (o.weapons != null && Array.IndexOf(o.weapons, d.Id) < 0) continue;
                tracks.Add(SimulateWeapon(d, config, tuning, mix, o, report));
            }

            report.weapons = tracks.ToArray();
            report.flags = BuildFlags(report, config).ToArray();
            report.notes = new[]
            {
                "MODEL fields (WP-045 calibrated: pickup 1.00, minSecondsPerEnemy 2.5 from QA-bot runs; hit / engaged kept at 0.70 / 0.30 — the bot's implied 0.15 / 0.60 reflect bot skill, see calibration-v1.md): hitFraction, region mix (head 25% ×2 / upper 45% ×1 / lower 30% ×0.8 = shot multiplier 1.19), greedy cheapest-core-card purchase, crowd multipliers (pierce 0.82^k, chain 0.5×0.75^k, explosion chance×scale×(radius/2.2), ricochet chance×0.6), engagedFraction × simultaneousAttackers for incoming damage, regen counted over the un-engaged time.",
                "EXACT fields: pool, tier / armour counts (SpawnMixSimulator), EnemyHealth / EnemyAttackDamage / round multipliers, GoldRewardCalculator, CostForTier + DEC-070 clamp, LevelRequirement, RoundCompletionXp / KillXp, SpecialWeaponSpawnChance, weapon settings, WeaponUpgradeTuning multipliers, roster core branches."
            };
            return report;
        }

        private static int ClampOpening(int cost, EconomyConfigSection economy)
        {
            int min = Mathf.Max(1, Mathf.Min(economy.StartingCostMin, economy.StartingCostMax));
            int max = Mathf.Max(min, Mathf.Max(economy.StartingCostMin, economy.StartingCostMax));
            return Mathf.Max(1, Mathf.Clamp(cost, min, max));
        }

        private const float HeadShare = 0.25f, UpperShare = 0.45f, LowerShare = 0.30f;   // MODEL: where the player's shots land (explosion shares reused)

        private static WeaponTrack SimulateWeapon(ClassicWeaponDefinition d, PrototypeConfig config, WeaponUpgradeTuning tuning,
            SpawnMixSimulator.Report mix, Options o, Report report)
        {
            ClassicWeaponSettings w = d.Settings(config);
            var track = new WeaponTrack
            {
                weapon = d.Id.ToString(), rosterKey = d.RosterKey, unlockLevel = d.UnlockLevel(config),
                coreBranches = d.CoreBranches != null ? string.Join("+", Array.ConvertAll(d.CoreBranches, b => b.ToString())) : string.Empty
            };
            if (w == null) return track;

            var tiers = new int[WeaponModifierState.BranchCount];
            var offered = new List<UpgradeBranch>();
            foreach (UpgradeBranch b in (UpgradeBranch[])Enum.GetValues(typeof(UpgradeBranch)))
            {
                if (WeaponRoster.IsBranchOffered(d.Id, b)) offered.Add(b);
            }

            float shotMultiplier = HeadShare * config.Enemies.Armor.ExposedHeadMultiplier + UpperShare * config.Enemies.Armor.ExposedUpperBodyMultiplier + LowerShare * config.Enemies.Armor.ExposedLowerBodyMultiplier;
            float[] durability = { config.Enemies.Armor.HelmetDurability, config.Enemies.Armor.BodyDurability, config.Enemies.Armor.LegDurability };
            float[] regionShare = { HeadShare, UpperShare, LowerShare };

            int wallet = 0, xpCumulative = 0;
            var rows = new List<RoundRow>();
            int firstBuy = 0, firstFullCore = 0, firstRisk = 0;
            foreach (SpawnMixSimulator.RoundResult mr in mix.rounds)
            {
                int round = mr.round;
                var row = new RoundRow { round = round, pool = mr.pool };
                Array.Copy(mr.tierCounts, row.tierCounts, TierCount);

                // ---- build → DPS
                float fireRateMult = Mathf.Min(1f + tuning.FireRateBonusPerTier * tiers[(int)UpgradeBranch.FireRate], Mathf.Max(1f, tuning.MaximumFireRateMultiplier));
                float reloadMult = Mathf.Max(tuning.MinimumReloadFraction, 1f - tuning.ReloadSpeedBonusPerTier * tiers[(int)UpgradeBranch.ReloadSpeed]);
                float rps = Mathf.Max(0.01f, w.FireRate * fireRateMult);
                float perShot = w.Damage * Mathf.Max(1, w.PelletCount);
                float magSeconds = w.MagazineSize / rps;
                float sustained = perShot * w.MagazineSize / (magSeconds + w.ReloadTime * reloadMult) * o.hitFraction;
                row.sustainedDps = sustained;
                row.crowdDps = sustained * CrowdMultiplier(tiers, tuning);

                // ---- TTK and incoming damage per tier, expected armour mix from the simulator
                float totalEffectiveHp = 0f;
                float incomingWeighted = 0f;
                int spawns = Math.Max(1, mr.spawns);
                for (int t = 0; t < TierCount; t++)
                {
                    var tier = (EnemyTier)t;
                    EnemyTierSettings ts = config.Enemies.Find(tier);
                    float hp = config.EnemyHealth(tier, round);
                    float baseEff = hp / shotMultiplier;
                    float fullArmour = baseEff + durability[0] + durability[1] + durability[2];
                    int n = mr.tierCounts[t];
                    float expectedDur = 0f;
                    if (n > 0)
                    {
                        for (int s = 0; s < 3; s++)
                        {
                            expectedDur += durability[s] * (mr.armour[s].equippedByTier[t] / (float)n);
                        }
                    }

                    float expectedEff = baseEff + expectedDur;
                    float singleDps = Mathf.Max(0.01f, row.sustainedDps);
                    float cycle = ts != null ? Mathf.Max(0.1f, ts.TelegraphTime + ts.AttackActiveTime + ts.AttackRecoveryTime + ts.AttackCooldown) : 1f;
                    float incoming = config.EnemyAttackDamage(tier, round) / cycle;
                    row.ttk[t] = new TierTtk
                    {
                        tier = report.tierNames[t], count = n, healthEffective = expectedEff,
                        ttkNoArmour = baseEff / singleDps, ttkArmoured = fullArmour / singleDps, ttkExpected = expectedEff / singleDps,
                        incomingDps = incoming
                    };
                    totalEffectiveHp += expectedEff * n;
                    incomingWeighted += incoming * n;
                    row.armouredShare[t] = n > 0 ? Mathf.Clamp01((mr.armour[0].equippedByTier[t] + mr.armour[1].equippedByTier[t] + mr.armour[2].equippedByTier[t]) / (float)n) : 0f;
                }

                float refillFloor = mr.pool / Mathf.Max(0.01f, config.Rounds.MaxSpawnsPerRefillTick / Mathf.Max(0.01f, config.Rounds.SpawnRefillInterval));
                row.pacingFloorSeconds = Mathf.Max(refillFloor, mr.pool * Mathf.Max(0f, o.minSecondsPerEnemy));
                row.clearSeconds = Mathf.Max(row.pacingFloorSeconds, totalEffectiveHp / Mathf.Max(0.01f, row.crowdDps));

                // ---- risk proxy: expected damage taken over the engaged time vs health + regen over the rest
                float avgIncoming = incomingWeighted / spawns;
                float damageTaken = avgIncoming * o.simultaneousAttackers * row.clearSeconds * o.engagedFraction;
                float maxHealth = config.Player.MaxHealth + config.Economy.Find(UpgradeBranch.MaximumHealth).EffectPerTier * tiers[(int)UpgradeBranch.MaximumHealth];
                float regenRate = config.Player.HealthRegenRate + config.Economy.Find(UpgradeBranch.RegenerationRate).EffectPerTier * tiers[(int)UpgradeBranch.RegenerationRate];
                float regenDelay = Mathf.Max(0f, config.Player.HealthRegenDelay - config.Economy.Find(UpgradeBranch.RegenerationDelay).EffectPerTier * tiers[(int)UpgradeBranch.RegenerationDelay]);
                float regenWindow = Mathf.Max(0f, row.clearSeconds * (1f - o.engagedFraction) - regenDelay);
                float budget = maxHealth + Mathf.Min(regenRate * regenWindow, maxHealth * 2f);
                row.riskProxy = budget > 0f ? damageTaken / budget : 99f;
                if (firstRisk == 0 && row.riskProxy >= 1f) firstRisk = round;

                // ---- gold: exact reward per spawn (tier + armour composition), pickup fraction
                float gold = 0f;
                for (int t = 0; t < TierCount; t++)
                {
                    int n = mr.tierCounts[t];
                    if (n == 0) continue;
                    float fam = GoldRewardCalculator.Compute((EnemyTier)t, SpawnArmorComposition.None, config).Total;
                    gold += fam * n;
                    gold += mr.armour[0].equippedByTier[t] * config.Economy.ArmorGoldBonusHelmet + mr.armour[1].equippedByTier[t] * config.Economy.ArmorGoldBonusBody + mr.armour[2].equippedByTier[t] * config.Economy.ArmorGoldBonusLeg;
                }

                row.goldIncome = Mathf.RoundToInt(gold * o.goldPickupFraction);
                wallet += row.goldIncome;

                // ---- purchase: cheapest affordable, not maxed, offered card; core first (MODEL)
                row.purchase = "-";
                if (o.buyEveryRound)
                {
                    UpgradeBranch best = UpgradeBranch.Piercing; int bestCost = int.MaxValue; bool found = false;
                    foreach (UpgradeBranch b in offered)
                    {
                        UpgradeBranchSettings bs = config.Economy.Find(b);
                        if (bs == null) continue;
                        int owned = tiers[(int)b];
                        if (owned >= bs.MaxTier) continue;
                        int cost = owned == 0 ? ClampOpening(bs.CostForTier(0), config.Economy) : bs.CostForTier(owned);
                        bool core = d.HasCoreBranch(b);
                        int rank = cost - (core ? 1000 : 0);          // core branches first, then cheapest
                        if (cost <= wallet && rank < bestCost) { bestCost = rank; best = b; found = true; }
                    }

                    if (found)
                    {
                        UpgradeBranchSettings bs = config.Economy.Find(best);
                        int owned = tiers[(int)best];
                        int cost = owned == 0 ? ClampOpening(bs.CostForTier(0), config.Economy) : bs.CostForTier(owned);
                        wallet -= cost; tiers[(int)best]++;
                        row.goldSpent = cost;
                        row.purchase = best + " t" + tiers[(int)best] + " " + cost + "g";
                        if (firstBuy == 0) firstBuy = round;
                    }
                }

                if (firstFullCore == 0 && d.CoreBranches != null && d.CoreBranches.Length > 0)
                {
                    bool all = true;
                    foreach (UpgradeBranch b in d.CoreBranches) all &= tiers[(int)b] > 0;
                    if (all) firstFullCore = round;
                }

                row.walletAfter = wallet;
                row.build = DescribeTiers(tiers);

                // ---- XP / level (fresh profile, single run reaching this round)
                int xp = config.RoundCompletionXp(round);
                for (int t = 0; t < TierCount; t++) xp += config.KillXp((EnemyTier)t) * mr.tierCounts[t];
                row.xpRound = xp; xpCumulative += xp; row.xpCumulative = xpCumulative;
                int level = 1, into = xpCumulative;
                while (level < 50 && into >= config.LevelRequirement(level)) { into -= config.LevelRequirement(level); level++; }
                row.profileLevelIfFirstRun = level;

                row.expectedSpecialPickups = config.SpecialWeaponSpawnChance(round) +
                    (round >= config.Weapons.Special.SecondSpawnFromRound ? config.Weapons.Special.SecondSpawnChance : 0f);
                rows.Add(row);
            }

            track.rounds = rows.ToArray();
            track.firstRiskRound = firstRisk;
            track.firstRoundWithAnyPurchase = firstBuy;
            track.firstRoundWithFullCoreTier1 = firstFullCore;
            return track;
        }

        /// <summary>MODEL: crowd damage multiplier of the on-hit branches (per-target extras, falloff applied).</summary>
        public static float CrowdMultiplier(int[] tiers, WeaponUpgradeTuning t)
        {
            float extra = 0f;
            int pierce = tiers[(int)UpgradeBranch.Piercing] * Mathf.Max(0, t.PierceTargetsPerTier);
            for (int k = 1; k <= pierce; k++) extra += Mathf.Pow(t.PierceDamageFalloff, k);
            int chain = tiers[(int)UpgradeBranch.Chain] * Mathf.Max(0, t.ChainTargetsPerTier);
            for (int k = 1; k <= chain; k++) extra += t.ChainDamageScale * Mathf.Pow(t.ChainDamageFalloff, k - 1);
            int ex = tiers[(int)UpgradeBranch.Explosion];
            if (ex > 0)
            {
                float chance = Mathf.Clamp01(t.ExplosionBaseChance + t.ExplosionChancePerTier * (ex - 1));
                float scale = t.ExplosionBaseDamageScale + t.ExplosionDamageScalePerTier * (ex - 1);
                float radius = t.ExplosionBaseRadius + t.ExplosionRadiusPerTier * (ex - 1);
                extra += chance * scale * (radius / Mathf.Max(0.1f, t.ExplosionBaseRadius));
            }

            int ric = tiers[(int)UpgradeBranch.Ricochet];
            if (ric > 0)
            {
                float chance = Mathf.Clamp01(t.RicochetBaseChance + t.RicochetChancePerTier * (ric - 1));
                int bounces = 1 + (ric - 1) / Mathf.Max(1, t.RicochetTiersPerExtraBounce);
                extra += chance * t.RicochetDamageScale * bounces;
            }

            return 1f + extra;
        }

        private static readonly string[] s_short = { "P", "R", "X", "C", "FR", "RS", "MS", "HP", "RD", "RR" };

        public static string DescribeTiers(int[] tiers)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < tiers.Length && i < s_short.Length; i++)
            {
                if (tiers[i] <= 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(s_short[i]).Append(tiers[i]);
            }

            return sb.Length == 0 ? "-" : sb.ToString();
        }

        // ------------------------------------------------------------------ bands

        /// <summary>Bands: DEC-070 (15..45 opening price), DEC-071 (one purchase per round is the model), DEC-021 (tier introduction / monotone shares), and the v1 model bands documented in the proposal.</summary>
        public static List<Flag> BuildFlags(Report r, PrototypeConfig config)
        {
            var flags = new List<Flag>();
            var branches = (UpgradeBranch[])Enum.GetValues(typeof(UpgradeBranch));
            for (int i = 0; i < branches.Length && i < 10; i++)
            {
                int raw = r.rawOpeningCardCost[i];
                if (raw < config.Economy.StartingCostMin || raw > config.Economy.StartingCostMax)
                {
                    flags.Add(new Flag { band = "DEC-070 opening price 15..45", severity = "warn", owner = "S-04",
                        text = branches[i] + " BaseCost " + raw + " is outside the opening band; the shop clamps it to " + r.openingCardCost[i] + " (clamp hides the authored value)." });
                }
            }

            foreach (WeaponTrack w in r.weapons)
            {
                if (w.rounds.Length == 0) continue;
                RoundRow first = w.rounds[0];
                if (first.ttk[0].ttkNoArmour > 2.0f)
                {
                    flags.Add(new Flag { band = "model TTK Standard R1 ≤ 2.0 s", severity = "warn", owner = "S-02",
                        text = w.weapon + ": Standard TTK at R1 = " + first.ttk[0].ttkNoArmour.ToString("0.00", CultureInfo.InvariantCulture) + " s (no armour)." });
                }

                if (w.firstRoundWithAnyPurchase == 0 || w.firstRoundWithAnyPurchase > 3)
                {
                    flags.Add(new Flag { band = "model first purchase by R3", severity = "out-of-band", owner = "S-04",
                        text = w.weapon + ": first affordable card at round " + w.firstRoundWithAnyPurchase + " (0 = never)." });
                }

                if (w.firstRiskRound > 0 && w.firstRiskRound < 6)
                {
                    flags.Add(new Flag { band = "model death-risk ≥1 not before R6", severity = "out-of-band", owner = "S-03",
                        text = w.weapon + ": risk proxy reaches 1.0 at round " + w.firstRiskRound + "." });
                }

                if (w.firstRiskRound == 0)
                {
                    flags.Add(new Flag { band = "model death-risk ≥1 by R15", severity = "warn", owner = "S-03",
                        text = w.weapon + ": risk proxy never reaches 1.0 within R1..R" + r.lastRound + " (runs may not end)." });
                }

                for (int i = 1; i < w.rounds.Length; i++)
                {
                    if (w.rounds[i].goldIncome < w.rounds[i - 1].goldIncome)
                    {
                        flags.Add(new Flag { band = "DEC-021 monotone income", severity = "info", owner = "S-04",
                            text = w.weapon + ": gold income dips at R" + w.rounds[i].round + " (" + w.rounds[i - 1].goldIncome + " → " + w.rounds[i].goldIncome + ")." });
                        break;
                    }
                }

                foreach (RoundRow row in w.rounds)
                {
                    if (row.ttk[3].count > 0 && row.ttk[3].ttkExpected > 12f)
                    {
                        flags.Add(new Flag { band = "model TTK Ultra ≤ 12 s", severity = "warn", owner = "S-02",
                            text = w.weapon + ": expected Ultra TTK at R" + row.round + " = " + row.ttk[3].ttkExpected.ToString("0.0", CultureInfo.InvariantCulture) + " s with build " + row.build + "." });
                        break;
                    }
                }
            }

            // Progression pacing (DEC-008): level 2 / 3 reachability within a first run that ends at the risk round.
            foreach (WeaponTrack w in r.weapons)
            {
                if (w.rosterKey != "A" || w.rounds.Length == 0) continue;
                int end = w.firstRiskRound > 0 ? w.firstRiskRound : w.rounds[w.rounds.Length - 1].round;
                int level = 1;
                foreach (RoundRow row in w.rounds) if (row.round <= end) level = row.profileLevelIfFirstRun;
                flags.Add(new Flag { band = "DEC-008 progression pacing", severity = level >= 2 ? "info" : "warn", owner = "S-04",
                    text = "A fresh profile ending its first run at R" + end + " reaches level " + level + " (B unlocks at level " + FindUnlock(r, "B") + ", C at " + FindUnlock(r, "C") + ")." });
            }

            return flags;
        }

        private static int FindUnlock(Report r, string key)
        {
            foreach (WeaponTrack w in r.weapons) if (w.rosterKey == key) return w.unlockLevel;
            return 0;
        }

        // ------------------------------------------------------------------ output

        public static string ToJson(Report report) => JsonUtility.ToJson(report, true);

        public static Report RunAndWrite(Options o, string jsonPath, string markdownPath)
        {
            Report r = Run(o);
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath));
            File.WriteAllText(jsonPath, ToJson(r));
            if (!string.IsNullOrEmpty(markdownPath)) File.WriteAllText(markdownPath, ToMarkdown(r));
            return r;
        }

        public static string ToMarkdown(Report r)
        {
            var sb = new StringBuilder();
            var ic = CultureInfo.InvariantCulture;
            sb.AppendLine("# Balance v1 — " + r.schema + " · seed " + r.seed + " · R" + r.firstRound + "–R" + r.lastRound + " · hit " + r.hitFraction.ToString("0.00", ic) + " · pickup " + r.goldPickupFraction.ToString("0.00", ic) + " · engaged " + r.engagedFraction.ToString("0.00", ic) + " × " + r.simultaneousAttackers + " · s/enemy " + r.minSecondsPerEnemy.ToString("0.0", ic));
            sb.AppendLine();
            sb.Append("Opening card prices (after DEC-070 clamp): ");
            var names = Enum.GetNames(typeof(UpgradeBranch));
            for (int i = 0; i < names.Length && i < 10; i++) sb.Append(names[i]).Append(' ').Append(r.openingCardCost[i]).Append(r.rawOpeningCardCost[i] != r.openingCardCost[i] ? "(" + r.rawOpeningCardCost[i] + ")" : string.Empty).Append(i + 1 < names.Length ? ", " : "\n");
            sb.Append("XP to reach level 2..6: ");
            for (int l = 1; l <= 5; l++) sb.Append("L").Append(l + 1).Append(' ').Append(r.xpToReachLevel[l - 1]).Append(l < 5 ? ", " : "\n\n");
            foreach (WeaponTrack w in r.weapons)
            {
                sb.AppendLine("## " + w.rosterKey + " · " + w.weapon + " (unlock L" + w.unlockLevel + ", core " + w.coreBranches + ") — first buy R" + w.firstRoundWithAnyPurchase + ", core complete R" + w.firstRoundWithFullCoreTier1 + ", risk≥1 at R" + w.firstRiskRound);
                sb.AppendLine();
                sb.AppendLine("| R | pool | S/Sp/E/U | armoured% | DPS single/crowd | clear s | TTK S/Sp/E/U (exp.) | gold +/− wallet | buy | build | XP cum → L | risk | special |");
                sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
                foreach (RoundRow row in w.rounds)
                {
                    sb.Append("| ").Append(row.round).Append(" | ").Append(row.pool).Append(" | ")
                      .Append(row.tierCounts[0]).Append('/').Append(row.tierCounts[1]).Append('/').Append(row.tierCounts[2]).Append('/').Append(row.tierCounts[3]).Append(" | ")
                      .Append(Mathf.RoundToInt(row.armouredShare[0] * 100)).Append('/').Append(Mathf.RoundToInt(row.armouredShare[1] * 100)).Append('/').Append(Mathf.RoundToInt(row.armouredShare[2] * 100)).Append('/').Append(Mathf.RoundToInt(row.armouredShare[3] * 100)).Append(" | ")
                      .Append(row.sustainedDps.ToString("0", ic)).Append('/').Append(row.crowdDps.ToString("0", ic)).Append(" | ")
                      .Append(row.clearSeconds.ToString("0", ic)).Append(" | ");
                    for (int t = 0; t < TierCount; t++) sb.Append(row.ttk[t].count > 0 ? row.ttk[t].ttkExpected.ToString("0.0", ic) : "·").Append(t < 3 ? "/" : " | ");
                    sb.Append('+').Append(row.goldIncome).Append("/−").Append(row.goldSpent).Append(' ').Append(row.walletAfter).Append(" | ")
                      .Append(row.purchase).Append(" | ").Append(row.build).Append(" | ")
                      .Append(row.xpCumulative).Append(" → L").Append(row.profileLevelIfFirstRun).Append(" | ")
                      .Append(row.riskProxy.ToString("0.00", ic)).Append(" | ")
                      .Append(row.expectedSpecialPickups.ToString("0.00", ic)).AppendLine(" |");
                }

                sb.AppendLine();
            }

            sb.AppendLine("## Flags");
            sb.AppendLine();
            foreach (Flag f in r.flags) sb.AppendLine("- **" + f.severity + "** [" + f.band + "] (" + f.owner + ") " + f.text);
            sb.AppendLine();
            foreach (string n in r.notes) sb.AppendLine("> " + n);
            return sb.ToString();
        }

        /// <summary>Batch entry: <c>-executeMethod DD.Prototype.Diagnostics.EditorTools.EconomySimulator.RunFromCommandLine -ddOut &lt;dir&gt; [-ddSeed n] [-ddLastRound n]</c>.</summary>
        public static void RunFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            string outDir = null; uint seed = 1u; int last = 15;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-ddOut") outDir = args[i + 1];
                else if (args[i] == "-ddSeed") uint.TryParse(args[i + 1], out seed);
                else if (args[i] == "-ddLastRound") int.TryParse(args[i + 1], out last);
            }

            if (string.IsNullOrEmpty(outDir)) throw new ArgumentException("-ddOut <dir> required");
            Report r = RunAndWrite(new Options { seed = seed, lastRound = last }, Path.Combine(outDir, "balance-v1.json"), Path.Combine(outDir, "balance-v1.md"));
            Debug.Log("[EconomySimulator] wrote " + outDir + " weapons=" + r.weapons.Length + " flags=" + r.flags.Length);
        }
    }
}
