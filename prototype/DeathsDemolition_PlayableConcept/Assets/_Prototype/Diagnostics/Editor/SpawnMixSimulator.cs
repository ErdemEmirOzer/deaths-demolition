using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using DD.Prototype.Characters;
using DD.Prototype.Core.Config;
using DD.Prototype.Enemies;
using DD.Prototype.Spawn;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Diagnostics.EditorTools
{
    /// <summary>
    /// WP-027 (ADR-027-1) · Spawn mix simulator: what the crowd of rounds 1..N is made of when
    /// the shipped policies run over N spawns per round, without a scene. Editor / test tool;
    /// never a Player path. Consumed by S-10's spawner statistics (WP-026) - the API below is
    /// the stable surface: <see cref="Options"/> in, <see cref="Report"/> out, <see cref="ToJson"/>.
    ///
    /// What is exact (the production code runs): the tier draw (<see cref="TierSelectionPolicy"/>
    /// with the round's real pool and the DEC-022 debut guarantee, one policy per round
    /// repetition), the per-slot armour odds (<c>PrototypeConfig.ArmorChance</c>, equip only when
    /// the slot has durability - <c>ZombieEnemy.TryFit</c>), the armour variants
    /// (<see cref="ArmorLookSelector"/>), the garment constraints of the equipped pieces
    /// (<see cref="ZombieLookConstraints.Build"/>) and the garment look
    /// (<see cref="ZombieLookSelector"/>) - all pure functions of the spawn seed.
    ///
    /// What is distribution-equivalent, not draw-identical: the spawn's <c>EnemyRandom</c> also
    /// feeds the silhouette (scale / lean) before the armour roll in the real body, so the
    /// simulator draws the armour Bernoulli trials from a fresh <c>EnemyRandom(spawnSeed)</c>
    /// instead of the body's post-silhouette state. Same probabilities, different stream; the
    /// look itself is the real one for that spawn seed.
    ///
    /// Rounds are simulated as whole pools (the guarantee lives per round): a round is repeated
    /// ceil(spawnsPerRound / pool) times, each repetition with a fresh policy, so
    /// "guaranteed debut exactly once per round" is asserted per repetition.
    /// </summary>
    public static class SpawnMixSimulator
    {
        public const string Schema = "dd-spawn-mix-v1";   // WP-028 added tierProfilesApplied / threeRegionByTier / forcedFitByTier (additive, ADR-027-1: no schema bump)
        public const int TierCount = 4;

        public sealed class Options
        {
            public PrototypeConfig config;               // null → PrototypeConfig.Active
            public ZombieModuleLibrary modules;          // null → CharacterAssetSet (Resources); may stay null → look section skipped
            public ZombieArmorLibrary armor;             // null → CharacterAssetSet (Resources); may stay null → armour variants/constraints skipped
            public int firstRound = 1;
            public int lastRound = 10;
            public int spawnsPerRound = 1000;            // minimum; rounded up to whole pools
            public uint seed = 1u;
            public float releaseFraction = new SpawnWeightTuning().GuaranteedIntroPoolFraction;   // production default (director tuning)
            public bool useTierProfiles = true;          // WP-028: apply the B/B/B version layer (armour tendency, forced fit); false = the WP-010/017 roll
            public TierBehaviourProfileSet profiles;     // null → TierBehaviourProfileSet.Active
        }

        [Serializable]
        public sealed class SlotCounts
        {
            public string slot;
            public int[] equippedByTier = new int[TierCount];
            public float[] expectedChanceByTier = new float[TierCount];   // config.ArmorChance(tier, slot, round)
        }

        [Serializable]
        public sealed class RoundResult
        {
            public int round;
            public int pool;                     // config.TotalEnemyPool(round)
            public int repetitions;
            public int spawns;
            public int[] tierCounts = new int[TierCount];
            public float[] tierShare = new float[TierCount];
            public float[] tierWeight = new float[TierCount];         // config.TierSpawnWeight(tier, round)
            public int[] guaranteedFlags = new int[TierCount];        // Guaranteed picks summed over repetitions
            public int[] debutOwed = new int[TierCount];              // repetitions in which the tier debuts (IntroductionRound == round)
            public int repetitionsWithWrongGuaranteeCount;            // a repetition where a debuting tier was flagged != 1 or a non-debuting tier flagged > 0
            public int shortfall;                                     // policy ShortfallCount summed
            public int forcedPicks;
            public SlotCounts[] armour = new SlotCounts[3];
            public int fullyArmoured;                                 // helmet + body + leg
            public int[] threeRegionByTier = new int[TierCount];      // WP-028: all three slots equipped, per tier
            public int[] forcedFitByTier = new int[TierCount];        // WP-028: looks carrying the version's forced fit
            public int unarmoured;
            // garments (skinned crowd; zero when no module library)
            public int lookSamples;
            public int colourwayPicks;                                // draws that had a colourway (cw1/cw2)
            public int fadedPicks;                                    // cw2 among colourwayPicks
            public int fadedForcedByArmour;                           // cw2 forced by a Present body / leg plate (subset of fadedPicks)
            public int minoCape;                                      // T7
            public int bald;                                          // H0
            public int forbiddenPairs;                                // T6 + B5 together
            public int invalidLooks;
            public int excludedModuleHits;                            // a look carrying a module its armour excludes (must be 0)
            // the same seeds drawn WITHOUT armour constraints (the WP-016 crowd the README ceilings were written for)
            public int freeColourwayPicks, freeFadedPicks, freeMinoCape, freeBald;

            // WP-055: what one Special use does to THIS round's crowd (tier-share weighted, SpecialReactionProfile):
            public float swPushEfficacy;                              // Kagura: mean push/daze resistance factor (1 = every body shoved fully)
            public float swWebHoldSeconds;                            // Ofuda: mean net seconds per netted body (SealSeconds × tier share of the seal)
            public float swWebRootShare;                              // Ofuda: share of the crowd that is fully rooted (Standard)
            public float swPullMovableShare;                          // Tide: share of the crowd the well actually drags (all but Ultra)

            // WP-055B · AP-011B / AP-007C crowd rules:
            public int armouredVividCloth;                            // cloth picks on an armoured spawn that are NOT cw2 (must be 0)
            public float vividShare;                                  // cw1 share of the constrained colourway picks (≥ MinVividShare)
            public int accessoryPicks, strawHats, strawHatWithArmour, strawHatOffStandard, strawHatWithMino, strawHatEligible;   // A1 rules (0 / 0 / 0; share of eligible Standards 15–25 %)
        }

        [Serializable]
        public sealed class Report
        {
            public string schema = Schema;
            public string utc;
            public uint seed;
            public float releaseFraction;
            public int spawnsPerRound;
            public bool lookSimulated;
            public bool tierProfilesApplied;                          // WP-028
            public bool armourVariantsSimulated;
            public string[] tierNames = { "Standard", "Special", "Elite", "Ultra" };
            public int[] introductionRound = new int[TierCount];
            public RoundResult[] rounds = Array.Empty<RoundResult>();
            public string[] violations = Array.Empty<string>();      // crowd rules broken (README ceilings on the free draw, forbidden pairs, exclusions, guarantee)
            public string[] notes = Array.Empty<string>();           // observations that are not rule breaks: armour-constrained crowd shares past a README ceiling (WP-017 rules bite)
            public int totalSpawns;
        }

        /// <summary>Run the simulation. Pure apart from reading the config / libraries; no scene, no allocation-sensitive path.</summary>
        public static Report Run(Options o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            PrototypeConfig config = o.config != null ? o.config : PrototypeConfig.Active;
            if (config == null) throw new InvalidOperationException("SpawnMixSimulator: no PrototypeConfig (pass Options.config or have an active asset).");

            ZombieModuleLibrary modules = o.modules;
            ZombieArmorLibrary armorLib = o.armor;
            if (modules == null || armorLib == null)
            {
                CharacterAssetSet set = CharacterAssetSet.Load();
                if (set != null)
                {
                    modules ??= set.ZombieLibrary;
                    armorLib ??= set.ZombieArmor;
                }
            }

            bool lookOn = modules != null && modules.modules != null && modules.modules.Length > 0 && modules.bodies != null && modules.bodies.Length > 0;
            bool armourVariantsOn = armorLib != null && armorLib.variants != null && armorLib.variants.Length > 0;
            TierBehaviourProfileSet profiles = o.useTierProfiles ? (o.profiles != null ? o.profiles : TierBehaviourProfileSet.Active) : null;

            var report = new Report
            {
                utc = DateTime.UtcNow.ToString("o"),
                seed = o.seed,
                releaseFraction = o.releaseFraction,
                spawnsPerRound = o.spawnsPerRound,
                lookSimulated = lookOn,
                armourVariantsSimulated = armourVariantsOn,
                tierProfilesApplied = profiles != null,
            };
            for (int t = 0; t < TierCount; t++)
            {
                report.introductionRound[t] = RoundDifficultyOverrideRules.IntroductionRoundOf(config, (EnemyTier)t);
            }

            int first = Mathf.Max(1, o.firstRound), last = Mathf.Max(first, o.lastRound);
            var rounds = new List<RoundResult>(last - first + 1);
            var violations = new List<string>();
            var notes = new List<string>();
            var policy = new TierSelectionPolicy();
            var slotNames = new[] { ArmorSlot.Helmet, ArmorSlot.Body, ArmorSlot.Leg };

            for (int round = first; round <= last; round++)
            {
                int pool = Mathf.Max(1, config.TotalEnemyPool(round));
                int reps = Mathf.Max(1, (o.spawnsPerRound + pool - 1) / pool);
                var r = new RoundResult { round = round, pool = pool, repetitions = reps };
                for (int s = 0; s < 3; s++)
                {
                    r.armour[s] = new SlotCounts { slot = slotNames[s].ToString() };
                    for (int t = 0; t < TierCount; t++) r.armour[s].expectedChanceByTier[t] = EnemyVersionApplier.ArmourChance(config, profiles != null ? profiles.For((EnemyTier)t) : null, (EnemyTier)t, slotNames[s], round);
                }

                for (int t = 0; t < TierCount; t++) r.tierWeight[t] = config.TierSpawnWeight((EnemyTier)t, round);

                for (int rep = 0; rep < reps; rep++)
                {
                    uint repSeed = Mix(o.seed ^ ((uint)round * 0x9E3779B9u) ^ ((uint)rep * 0x85EBCA6Bu));
                    var rng = new SeededTierRandom(repSeed);
                    policy.BeginRound(round, config, pool, o.releaseFraction);
                    var flagsThisRep = new int[TierCount];

                    for (int i = 0; i < pool; i++)
                    {
                        TierPick pick = policy.Next(config, i, pool - i, rng);
                        int t = (int)pick.Tier;
                        r.tierCounts[t]++;
                        r.spawns++;
                        if (pick.Guaranteed) { r.guaranteedFlags[t]++; flagsThisRep[t]++; }
                        if (pick.Forced) r.forcedPicks++;

                        uint spawnSeed = Mix(repSeed ^ ((uint)(i + 1) * 0xC2B2AE35u));
                        var body = new EnemyRandom(spawnSeed);
                        TierBehaviourProfile profile = profiles != null ? profiles.For(pick.Tier) : null;
                        bool h = Roll(config, profile, ref body, pick.Tier, ArmorSlot.Helmet, round);   // Helmet, Body, Leg - ZombieEnemy.RollArmor order
                        bool b = Roll(config, profile, ref body, pick.Tier, ArmorSlot.Body, round);
                        bool l = Roll(config, profile, ref body, pick.Tier, ArmorSlot.Leg, round);
                        if (h) r.armour[0].equippedByTier[t]++;
                        if (b) r.armour[1].equippedByTier[t]++;
                        if (l) r.armour[2].equippedByTier[t]++;
                        if (h && b && l) { r.fullyArmoured++; r.threeRegionByTier[t]++; }
                        if (!h && !b && !l) r.unarmoured++;

                        if (!lookOn) continue;
                        ArmorLook armorLook = armourVariantsOn ? ArmorLookSelector.Select(armorLib, spawnSeed) : default;
                        ZombieLookConstraints c = armourVariantsOn ? ZombieLookConstraints.Build(armorLib, armorLook, h, b, l, pick.Tier == EnemyTier.Standard) : ZombieLookConstraints.None;
                        ZombieLook look = ZombieLookSelector.Select(modules, spawnSeed, c);
                        if (profile != null && profile.ForcesFit && HasFit(modules, profile.ForcedFit)) { look.fit = profile.ForcedFit; r.forcedFitByTier[t]++; }   // ZombieAppearance.WithVersionFit
                        r.lookSamples++;
                        if (!look.IsValid) { r.invalidLooks++; continue; }
                        ZombieLook free = c.IsEmpty ? look : ZombieLookSelector.Select(modules, spawnSeed, ZombieLookConstraints.None);
                        CountFreeColourway(modules, free.top, free.topColourway, r);
                        CountFreeColourway(modules, free.bottom, free.bottomColourway, r);
                        if (free.top == ZombieModuleLibrary.TopMinoCape) r.freeMinoCape++;
                        if (free.head == ZombieModuleLibrary.HeadNone) r.freeBald++;
                        CountColourway(modules, look.top, look.topColourway, c.fadedTop, r);
                        CountColourway(modules, look.bottom, look.bottomColourway, c.fadedBottom, r);
                        if (look.top == ZombieModuleLibrary.TopMinoCape) r.minoCape++;
                        if (look.head == ZombieModuleLibrary.HeadNone) r.bald++;
                        if (look.top == ZombieModuleLibrary.TopBareStraps && look.bottom == ZombieModuleLibrary.BottomFundoshi) r.forbiddenPairs++;
                        if (c.ExcludesTop(look.top) || c.ExcludesBottom(look.bottom) || c.ExcludesHead(look.head)) r.excludedModuleHits++;
                        // WP-055B · AP-011B: any plate ⇒ cw2 top + bottom (counted where the cloth has colourways)
                        if (h || b || l)
                        {
                            if (modules.FindModule(look.top).HasColourways && look.topColourway != ZombieModuleLibrary.ColourwayFaded) r.armouredVividCloth++;
                            if (modules.FindModule(look.bottom).HasColourways && look.bottomColourway != ZombieModuleLibrary.ColourwayFaded) r.armouredVividCloth++;
                        }
                        // WP-055B · AP-007C accessory slot (only when the library carries it)
                        if (look.HasAccessory)
                        {
                            r.accessoryPicks++;
                            if (look.accessory == ZombieModuleLibrary.AccessoryStrawHat)
                            {
                                r.strawHats++;
                                if (h || b || l) r.strawHatWithArmour++;
                                if (pick.Tier != EnemyTier.Standard) r.strawHatOffStandard++;
                                if (look.top == ZombieModuleLibrary.TopMinoCape) r.strawHatWithMino++;
                            }
                        }
                        if (pick.Tier == EnemyTier.Standard && !h && !b && !l && look.top != ZombieModuleLibrary.TopMinoCape) r.strawHatEligible++;
                    }

                    r.shortfall += policy.ShortfallCount;
                    bool wrong = false;
                    for (int t = 1; t < TierCount; t++)
                    {
                        bool debuts = report.introductionRound[t] == round;
                        if (debuts) r.debutOwed[t]++;
                        if (debuts ? flagsThisRep[t] != 1 : flagsThisRep[t] != 0) wrong = true;
                    }

                    if (wrong) r.repetitionsWithWrongGuaranteeCount++;
                    policy.Clear();
                }

                for (int t = 0; t < TierCount; t++) r.tierShare[t] = r.spawns > 0 ? (float)r.tierCounts[t] / r.spawns : 0f;

                // WP-055: Special usage effect on this crowd — the same tier profile the bodies resolve at runtime.
                float sealSeconds = 6f;   // WP-069 B4 interim (S-02): the removed Ofuda Loom's seal seconds; the web metric leaves with S-03's reaction clean-up
                for (int t = 0; t < TierCount; t++)
                {
                    var tier = (EnemyTier)t;
                    r.swPushEfficacy += r.tierShare[t] * SpecialReactionProfile.PushResistance(tier);
                    r.swWebHoldSeconds += r.tierShare[t] * SpecialReactionProfile.WebSeconds(tier, sealSeconds);
                    if (SpecialReactionProfile.WebFactor(tier) <= 0f) r.swWebRootShare += r.tierShare[t];
                    if (SpecialReactionProfile.PullMoves(tier)) r.swPullMovableShare += r.tierShare[t];
                }

                // Rule checks per round (DEC-022 guarantee; README crowd ceilings; forbidden pair; armour exclusions).
                if (r.repetitionsWithWrongGuaranteeCount > 0) violations.Add("round " + round + ": guaranteed debut flag count != expected in " + r.repetitionsWithWrongGuaranteeCount + "/" + reps + " repetitions");
                for (int t = 1; t < TierCount; t++)
                {
                    if (round < report.introductionRound[t] && r.tierCounts[t] > 0) violations.Add("round " + round + ": " + report.tierNames[t] + " spawned before its introduction round " + report.introductionRound[t]);
                }

                if (r.forbiddenPairs > 0) violations.Add("round " + round + ": T6 bare straps + B5 fundoshi together " + r.forbiddenPairs + "x");
                if (r.invalidLooks > 0) violations.Add("round " + round + ": invalid looks " + r.invalidLooks);
                if (r.excludedModuleHits > 0) violations.Add("round " + round + ": look carries an armour-excluded module " + r.excludedModuleHits + "x");
                if (r.lookSamples > 0)
                {
                    // README ceilings (AP-007 spawner rule) govern the WP-016 free draw; the armour-constrained crowd
                    // (WP-017: plates exclude modules and force cw2) is reported as a note when it passes a ceiling.
                    float minoFree = (float)r.freeMinoCape / r.lookSamples, baldFree = (float)r.freeBald / r.lookSamples;
                    float fadedFree = r.freeColourwayPicks > 0 ? (float)r.freeFadedPicks / r.freeColourwayPicks : 0f;
                    if (minoFree > ZombieLookSelector.MaxMinoShare) violations.Add("round " + round + ": T7 mino share (free draw) " + F(minoFree) + " > " + F(ZombieLookSelector.MaxMinoShare));
                    if (baldFree > ZombieLookSelector.MaxBaldShare) violations.Add("round " + round + ": H0 bald share (free draw) " + F(baldFree) + " > " + F(ZombieLookSelector.MaxBaldShare));
                    if (fadedFree > ZombieLookSelector.MaxFadedShare) violations.Add("round " + round + ": cw2 share (free draw) " + F(fadedFree) + " > " + F(ZombieLookSelector.MaxFadedShare));
                    float mino = (float)r.minoCape / r.lookSamples, bald = (float)r.bald / r.lookSamples;
                    float faded = r.colourwayPicks > 0 ? (float)r.fadedPicks / r.colourwayPicks : 0f;
                    if (mino > ZombieLookSelector.MaxMinoShare) notes.Add("round " + round + ": armour-constrained T7 share " + F(mino) + " > " + F(ZombieLookSelector.MaxMinoShare) + " (free " + F(minoFree) + ")");
                    if (bald > ZombieLookSelector.MaxBaldShare) notes.Add("round " + round + ": armour-constrained H0 share " + F(bald) + " > " + F(ZombieLookSelector.MaxBaldShare) + " (free " + F(baldFree) + "; helmets exclude heads)");
                    if (faded > ZombieLookSelector.MaxFadedShare) notes.Add("round " + round + ": armour-constrained cw2 share " + F(faded) + " > " + F(ZombieLookSelector.MaxFadedShare) + " (free " + F(fadedFree) + "; " + r.fadedForcedByArmour + " forced under plates)");
                }

                // WP-055B: crowd identity rules
                r.vividShare = r.colourwayPicks > 0 ? 1f - (float)r.fadedPicks / r.colourwayPicks : 1f;
                if (r.armouredVividCloth > 0) violations.Add("round " + round + ": " + r.armouredVividCloth + " cloth picks on armoured spawns are not cw2 (AP-011B)");
                if (r.lookSamples > 0 && r.colourwayPicks > 0 && r.vividShare < ZombieLookSelector.MinVividShareFor(round)) violations.Add("round " + round + ": vivid cw1 share " + F(r.vividShare) + " < " + F(ZombieLookSelector.MinVividShareFor(round)) + " (AP-011B identity, R1–R7)");
                else if (r.lookSamples > 0 && r.colourwayPicks > 0 && round >= ZombieLookSelector.LateVividRound && r.vividShare < ZombieLookSelector.MinVividShare) notes.Add("round " + round + ": vivid cw1 share " + F(r.vividShare) + " < " + F(ZombieLookSelector.MinVividShare) + " — Ö-5 armour bands (cw2 by decision), reported not enforced from R8");
                if (r.strawHatWithArmour > 0 || r.strawHatOffStandard > 0 || r.strawHatWithMino > 0) violations.Add("round " + round + ": A1 straw hat with armour / off-Standard / mino " + r.strawHatWithArmour + "/" + r.strawHatOffStandard + "/" + r.strawHatWithMino + " (AP-007C)");
                if (r.accessoryPicks > 0 && r.strawHatEligible >= 50)
                {
                    float hatShare = (float)r.strawHats / r.strawHatEligible;
                    if (hatShare < ZombieLookSelector.MinStrawHatShare || hatShare > ZombieLookSelector.MaxStrawHatShare) notes.Add("round " + round + ": A1 straw hat share of eligible Standards " + F(hatShare) + " outside " + F(ZombieLookSelector.MinStrawHatShare) + "–" + F(ZombieLookSelector.MaxStrawHatShare) + " (AP-007C AD band)");
                }

                report.totalSpawns += r.spawns;
                rounds.Add(r);
            }

            report.rounds = rounds.ToArray();
            report.violations = violations.ToArray();
            report.notes = notes.ToArray();
            return report;
        }

        public static string ToJson(Report report) => JsonUtility.ToJson(report, true);

        /// <summary>Run with the given options and write the JSON report; returns the report.</summary>
        public static Report RunAndWrite(Options o, string path)
        {
            Report report = Run(o);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, ToJson(report), new UTF8Encoding(false));
            return report;
        }

        /// <summary>One-line summary for logs / notes.</summary>
        public static string Summarize(Report report)
        {
            var sb = new StringBuilder();
            sb.Append("SpawnMix seed=").Append(report.seed).Append(" rounds=").Append(report.rounds.Length).Append(" spawns=").Append(report.totalSpawns)
              .Append(" tierProfiles=").Append(report.tierProfilesApplied).Append(" look=").Append(report.lookSimulated).Append(" armourVariants=").Append(report.armourVariantsSimulated).Append(" violations=").Append(report.violations.Length).Append('\n');
            for (int i = 0; i < report.rounds.Length; i++)
            {
                RoundResult r = report.rounds[i];
                sb.Append("R").Append(r.round).Append(" pool=").Append(r.pool).Append(" x").Append(r.repetitions).Append(" n=").Append(r.spawns)
                  .Append(" S/Sp/E/U=").Append(F(r.tierShare[0])).Append('/').Append(F(r.tierShare[1])).Append('/').Append(F(r.tierShare[2])).Append('/').Append(F(r.tierShare[3]))
                  .Append(" debutFlags=").Append(r.guaranteedFlags[1]).Append('/').Append(r.guaranteedFlags[2]).Append('/').Append(r.guaranteedFlags[3])
                  .Append(" armour H/B/L=").Append(Share(r.armour[0], r.spawns)).Append('/').Append(Share(r.armour[1], r.spawns)).Append('/').Append(Share(r.armour[2], r.spawns))
                  .Append(" eliteBody=").Append(F(r.tierCounts[2] > 0 ? (float)r.armour[1].equippedByTier[2] / r.tierCounts[2] : 0f))
                  .Append(" ultra3=").Append(F(r.tierCounts[3] > 0 ? (float)r.threeRegionByTier[3] / r.tierCounts[3] : 0f)).Append(" ultraFitC=").Append(r.forcedFitByTier[3]).Append('/').Append(r.tierCounts[3])
                  .Append(" sw push=").Append(F(r.swPushEfficacy)).Append(" web=").Append(F(r.swWebHoldSeconds)).Append("s(root ").Append(F(r.swWebRootShare)).Append(") pull=").Append(F(r.swPullMovableShare))   // WP-055
                  .Append(" vivid=").Append(F(r.vividShare)).Append(r.accessoryPicks > 0 ? " A1=" + r.strawHats + "/" + r.strawHatEligible : "");   // WP-055B
                if (r.lookSamples > 0)
                {
                    sb.Append(" cw2=").Append(F(r.colourwayPicks > 0 ? (float)r.fadedPicks / r.colourwayPicks : 0f)).Append("(forced ").Append(r.fadedForcedByArmour).Append(')')
                      .Append(" T7=").Append(F((float)r.minoCape / r.lookSamples)).Append(" H0=").Append(F((float)r.bald / r.lookSamples)).Append(" forbidden=").Append(r.forbiddenPairs);
                }

                sb.Append('\n');
            }

            for (int i = 0; i < report.violations.Length; i++) sb.Append("! ").Append(report.violations[i]).Append('\n');
            for (int i = 0; i < report.notes.Length; i++) sb.Append("~ ").Append(report.notes[i]).Append('\n');
            return sb.ToString();
        }

        [MenuItem("DD/Spawn/Simulate Spawn Mix (R1-10 x 1000)")]
        private static void MenuRun()
        {
            string path = Path.Combine(Application.dataPath, "..", "Temp", "dd-spawn-mix.json");
            Report report = RunAndWrite(new Options(), path);
            Debug.Log("[SpawnMix] " + Path.GetFullPath(path) + "\n" + Summarize(report));
        }

        private static bool HasFit(ZombieModuleLibrary modules, string fit)
        {
            for (int i = 0; i < modules.bodies.Length; i++) if (modules.bodies[i] != null && modules.bodies[i].fit == fit) return true;
            return false;
        }

        private static bool Roll(PrototypeConfig config, TierBehaviourProfile profile, ref EnemyRandom body, EnemyTier tier, ArmorSlot slot, int round)
        {
            float chance = EnemyVersionApplier.ArmourChance(config, profile, tier, slot, round);   // WP-028: same odds the spawn roll uses
            bool hit = body.Chance(chance);   // one draw per slot, the stream advances (ZombieEnemy.TryFit)
            return hit && config.Enemies.Armor != null && config.Enemies.Armor.DurabilityFor(slot) > 0f;
        }

        private static void CountColourway(ZombieModuleLibrary modules, string id, string colourway, bool forced, RoundResult r)
        {
            ZombieModuleLibrary.Module m = modules.FindModule(id);
            if (m == null || !m.HasColourways) return;
            r.colourwayPicks++;
            if (colourway == ZombieModuleLibrary.ColourwayFaded)
            {
                r.fadedPicks++;
                if (forced) r.fadedForcedByArmour++;
            }
        }

        private static void CountFreeColourway(ZombieModuleLibrary modules, string id, string colourway, RoundResult r)
        {
            ZombieModuleLibrary.Module m = modules.FindModule(id);
            if (m == null || !m.HasColourways) return;
            r.freeColourwayPicks++;
            if (colourway == ZombieModuleLibrary.ColourwayFaded) r.freeFadedPicks++;
        }

        private static string Share(SlotCounts s, int n)
        {
            int sum = 0;
            for (int t = 0; t < s.equippedByTier.Length; t++) sum += s.equippedByTier[t];
            return F(n > 0 ? (float)sum / n : 0f);
        }

        private static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);

        /// <summary>splitmix32-style finalizer (same family as SeededTierRandom's seed mixing).</summary>
        private static uint Mix(uint x)
        {
            x += 0x9E3779B9u;
            x ^= x >> 16; x *= 0x85EBCA6Bu;
            x ^= x >> 13; x *= 0xC2B2AE35u;
            x ^= x >> 16;
            return x;
        }
    }
}
