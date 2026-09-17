using System.Collections.Generic;
using System.Reflection;
using DD.Prototype.Core.Config;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Diagnostics.EditorTools
{
    /// <summary>
    /// WP-050 · DEC-021 authored table + Ö-2 armour curve, written to the SHIPPED config asset
    /// from ONE source (this file), so the values are reviewable in code, asserted by tests
    /// (<c>RoundTableTests</c>) and applied to the asset by a deterministic editor step instead
    /// of hand-edited YAML. User approval 2026-09-17 (WP-041 proposal Ö-2 / Ö-5); DEC-085 is
    /// recorded by S-04.
    ///
    ///   Ö-2  ArmorChanceSettings: Standard / Runner growth 0.025 → 0.012, max 0.75 → 0.55;
    ///        Guard growth 0.03 → 0.015 (entry odds 0.45 / 0.5 / 0.35 and max 0.9 kept);
    ///        Executioner unchanged. Target: Standard armoured share ≈ 50 % at R10, ≈ 65 % at R15.
    ///   Ö-5  Rows R8 / R11 / R14: an armour JUMP for the Standard + Runner crowd only
    ///        (ArmorTiers scope; Guard / Executioner keep their own odds) plus an Executioner
    ///        share (tier weight above the formula, forced minimum from R11). Row values stay ≤ the Ö-2
    ///        cap 0.55 per slot (S-04 note, WP-041). PROTOTYPE-ONLY values.
    ///
    /// Batch: -executeMethod DD.Prototype.Diagnostics.EditorTools.RoundTableAuthoring.Apply
    /// Menu:  DD/Config/Apply DEC-021 table (WP-050)
    /// </summary>
    public static class RoundTableAuthoring
    {
        public const string AssetPath = "Assets/_Prototype/Core/Resources/PrototypeConfig.asset";

        // ------------------------------------------------------------------ Ö-2
        public const float StandardGrowth = 0.012f, StandardMax = 0.55f;
        public const float GuardGrowth = 0.015f;

        /// <summary>The Ö-5 rows (fresh instances each call; the asset gets its own copies).</summary>
        public static List<RoundDifficultyOverride> Rows()
        {
            return new List<RoundDifficultyOverride>
            {
                Row(8, 0.40f, 0.36f, 0.30f, ultraWeight: 6f, ultraForced: 0),     // debut round: the REQ-071 guarantee already forces one
                Row(11, 0.48f, 0.44f, 0.38f, ultraWeight: 9f, ultraForced: 2),
                Row(14, 0.55f, 0.50f, 0.45f, ultraWeight: 12f, ultraForced: 3),   // ≤ the Ö-2 cap (S-04 note): a row never exceeds MaxChance 0.55 per slot
            };
        }

        private static RoundDifficultyOverride Row(int round, float helmet, float body, float leg, float ultraWeight, int ultraForced)
        {
            var row = new RoundDifficultyOverride
            {
                Round = round,
                OverrideArmorChances = true,
                ArmorChances = new ArmorChanceSettings { HelmetChance = helmet, BodyChance = body, LegChance = leg, ChanceGrowthPerRound = 0f, MaxChance = StandardMax },   // the row cannot exceed the Ö-2 cap
                ArmorTiers = new List<EnemyTier> { EnemyTier.Standard, EnemyTier.Special },
                OverrideTierWeights = true,
                TierWeights = new List<TierWeightOverride> { new TierWeightOverride { Tier = EnemyTier.Ultra, Weight = ultraWeight } },
                OverrideForcedCounts = ultraForced > 0,
                ForcedTierCounts = new List<TierCountOverride>(),
            };
            if (ultraForced > 0) row.ForcedTierCounts.Add(new TierCountOverride { Tier = EnemyTier.Ultra, Count = ultraForced });
            return row;
        }

        /// <summary>Apply Ö-2 to a config's tier table (code or asset instance).</summary>
        public static void ApplyArmourCurve(PrototypeConfig config)
        {
            foreach (EnemyTierSettings t in config.Enemies.Tiers)
            {
                if (t == null || t.ArmorChances == null) continue;
                switch (t.Tier)
                {
                    case EnemyTier.Standard:
                    case EnemyTier.Special:
                        t.ArmorChances.ChanceGrowthPerRound = StandardGrowth;
                        t.ArmorChances.MaxChance = StandardMax;
                        break;
                    case EnemyTier.Elite:
                        t.ArmorChances.ChanceGrowthPerRound = GuardGrowth;
                        break;
                }
            }
        }

        /// <summary>Write the rows into a config instance (private list, reflection — the runtime API refuses the active asset by design).</summary>
        public static List<string> ApplyRows(PrototypeConfig config)
        {
            FieldInfo f = typeof(PrototypeConfig).GetField("_roundDifficultyOverrides", BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(config, Rows());
            return RoundDifficultyOverrideRules.Validate(config);
        }

        [MenuItem("DD/Config/Apply DEC-021 table (WP-050)")]
        public static void Apply()
        {
            var asset = AssetDatabase.LoadAssetAtPath<PrototypeConfig>(AssetPath);
            if (asset == null)
            {
                Debug.LogError("[RoundTableAuthoring] asset not found: " + AssetPath);
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }

            ApplyArmourCurve(asset);
            List<string> failures = ApplyRows(asset);
            if (failures.Count > 0)
            {
                Debug.LogError("[RoundTableAuthoring] rows refused:\n" + string.Join("\n", failures));
                if (Application.isBatchMode) EditorApplication.Exit(3);
                return;
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("[RoundTableAuthoring] done: rows " + asset.RoundDifficultyOverrides.Count + " (R8/R11/R14), Ö-2 curve applied → " + AssetPath);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
