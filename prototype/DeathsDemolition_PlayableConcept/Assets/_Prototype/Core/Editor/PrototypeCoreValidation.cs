using System.Collections.Generic;
using System.Text;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using UnityEditor;
using UnityEngine;

namespace DD.Prototype.Core.EditorTools
{
    /// <summary>
    /// Headless sanity check for the core infrastructure. Proves the config asset
    /// loads, the request's formulas produce the request's numbers, the origin-tag
    /// reflection index resolves and the DEC-021 override rows validate (the shipped
    /// table is empty; the check is "rows valid", not "table empty").
    ///
    /// Not a substitute for the play-mode smoke test; it is the compile-time gate.
    /// Run from the menu or headless:
    ///   -executeMethod DD.Prototype.Core.EditorTools.PrototypeCoreValidation.Run
    /// </summary>
    public static class PrototypeCoreValidation
    {
        [MenuItem("Tools/Deaths Demolition/Validate Prototype Core", priority = 40)]
        public static void Run()
        {
            var failures = new List<string>();
            var report = new StringBuilder(1024);
            report.AppendLine("[Validate] Deaths Demolition prototype core");

            var config = AssetDatabase.LoadAssetAtPath<PrototypeConfig>(
                "Assets/_Prototype/Core/Resources/PrototypeConfig.asset");

            if (config == null)
            {
                Debug.LogError("[Validate] FAIL - PrototypeConfig asset not found.");
                return;
            }

            // Request formula: 12 + 6*(R-1) + floor(2.5*(R-1)^1.15)
            Check(failures, "TotalEnemyPool(1) == 12", config.TotalEnemyPool(1) == 12, config.TotalEnemyPool(1));
            Check(failures, "TotalEnemyPool(2) == 20", config.TotalEnemyPool(2) == 20, config.TotalEnemyPool(2));
            // 12 + 6*4 + floor(2.5 * 4^1.15) = 12 + 24 + floor(12.31) = 48
            Check(failures, "TotalEnemyPool(5) == 48", config.TotalEnemyPool(5) == 48, config.TotalEnemyPool(5));

            // Request formula: min(7 + 2R, 32), clamped by the absolute ceiling of 40.
            Check(failures, "TargetActiveEnemies(1) == 9", config.TargetActiveEnemies(1) == 9, config.TargetActiveEnemies(1));
            Check(failures, "TargetActiveEnemies(20) == 32", config.TargetActiveEnemies(20) == 32, config.TargetActiveEnemies(20));
            Check(failures, "AbsoluteSafetyCeiling == 40", config.AbsoluteSafetyCeiling == 40, config.AbsoluteSafetyCeiling);

            Check(failures, "EnemyHealthMultiplier(1) == 1", Mathf.Approximately(config.EnemyHealthMultiplier(1), 1f),
                config.EnemyHealthMultiplier(1));
            Check(failures, "EnemyHealthMultiplier(6) == 1.6", Mathf.Abs(config.EnemyHealthMultiplier(6) - 1.6f) < 1e-4f,
                config.EnemyHealthMultiplier(6));
            Check(failures, "EnemyDamageMultiplier(6) == 1.4", Mathf.Abs(config.EnemyDamageMultiplier(6) - 1.4f) < 1e-4f,
                config.EnemyDamageMultiplier(6));

            Check(failures, "Standard health at round 1 == 34", Mathf.Abs(config.EnemyHealth(EnemyTier.Standard, 1) - 34f) < 1e-3f,
                config.EnemyHealth(EnemyTier.Standard, 1));
            Check(failures, "Ultra health multiplier == 5x standard",
                Mathf.Abs(config.EnemyHealth(EnemyTier.Ultra, 1) - 170f) < 1e-2f, config.EnemyHealth(EnemyTier.Ultra, 1));

            // Request: 15 + 5 * round, level requirement 100 + 75 * (Level - 1)
            Check(failures, "RoundCompletionXp(4) == 35", config.RoundCompletionXp(4) == 35, config.RoundCompletionXp(4));
            Check(failures, "LevelRequirement(1) == 100", config.LevelRequirement(1) == 100, config.LevelRequirement(1));
            Check(failures, "LevelRequirement(4) == 325", config.LevelRequirement(4) == 325, config.LevelRequirement(4));

            Check(failures, "KillXp(Ultra) == 9", config.KillXp(EnemyTier.Ultra) == 9, config.KillXp(EnemyTier.Ultra));
            Check(failures, "GoldReward(Elite) == 10", config.GoldReward(EnemyTier.Elite) == 10, config.GoldReward(EnemyTier.Elite));

            // Special Weapon: base 35%, capped at 65%, nothing before round 4.
            Check(failures, "SpecialWeaponSpawnChance(3) == 0", Mathf.Approximately(config.SpecialWeaponSpawnChance(3), 0f),
                config.SpecialWeaponSpawnChance(3));
            Check(failures, "SpecialWeaponSpawnChance(4) == 0.35",
                Mathf.Abs(config.SpecialWeaponSpawnChance(4) - 0.35f) < 1e-4f, config.SpecialWeaponSpawnChance(4));
            Check(failures, "SpecialWeaponSpawnChance(40) <= 0.65", config.SpecialWeaponSpawnChance(40) <= 0.65f + 1e-4f,
                config.SpecialWeaponSpawnChance(40));

            // Explosion shares must add up to 1.
            ExplosionConfigSection explosion = config.Enemies.Explosion;
            float shareSum = explosion.HeadShare + explosion.UpperBodyShare + explosion.LowerBodyShare;
            Check(failures, "Explosion shares sum to 1.0", Mathf.Abs(shareSum - 1f) < 1e-4f, shareSum);

            // DEC-021 hybrid table (WP-010): rows, if any, must be valid. The shipped asset of
            // this package carries no row (reported, not required - authoring rows is a
            // product decision that has not been taken).
            System.Collections.Generic.List<string> overrideFailures = RoundDifficultyOverrideRules.Validate(config);
            Check(failures, "DEC-021 override rows (if any) are valid", overrideFailures.Count == 0,
                overrideFailures.Count == 0 ? "ok" : string.Join("; ", overrideFailures));
            Debug.Log("[Validation] DEC-021 override rows in the shipped asset: " + config.RoundDifficultyOverrides.Count + " (empty = formula only).");

            // Live contract: NotifyChanged must reach a subscriber and bump the revision.
            int received = 0;
            int startRevision = config.Revision;
            void Handler(PrototypeConfig _) => received++;
            config.Changed += Handler;
            config.NotifyChanged();
            config.Changed -= Handler;
            Check(failures, "Changed event fired once", received == 1, received);
            Check(failures, "Revision advanced", config.Revision == startRevision + 1, config.Revision);

            var watcher = new ConfigRevisionWatcher();
            bool first = watcher.Consume(config);
            bool second = watcher.Consume(config);
            config.NotifyChanged();
            bool third = watcher.Consume(config);
            Check(failures, "ConfigRevisionWatcher primes, settles, then reacts", first && !second && third,
                $"{first}/{second}/{third}");

            // Origin tags. Two scopes on purpose:
            //   the config walk answers "does every value in the asset carry a tag",
            //   the census answers "how many tagged values exist at all", including the
            //   per-system tuning objects the asset knows nothing about. Counting only the
            //   first one is how two thirds of the invented values stayed invisible.
            List<ConfigFieldBinding> bindings = ConfigFieldIndex.Build(config);
            Check(failures, "Config field index is populated", bindings.Count > 100, bindings.Count);

            int tagged = 0;
            int invented = 0;
            var untagged = new List<string>();
            var configKeys = new HashSet<string>();
            for (int i = 0; i < bindings.Count; i++)
            {
                ConfigFieldBinding b = bindings[i];
                if (b.Origin == null)
                {
                    untagged.Add(b.Path);
                    continue;
                }

                tagged++;
                configKeys.Add(b.DeclarationKey);
                if (b.Origin.Invented)
                {
                    invented++;
                }
            }

            IReadOnlyList<PrototypeOnlyDeclaration> declarations = PrototypeOnlyCensus.Declarations;
            int outside = 0;
            int outsideInvented = 0;
            int outsideWithoutPendingLine = 0;
            for (int i = 0; i < declarations.Count; i++)
            {
                PrototypeOnlyDeclaration declaration = declarations[i];
                if (configKeys.Contains(declaration.Key))
                {
                    continue;
                }

                outside++;
                if (declaration.Origin.Invented)
                {
                    outsideInvented++;
                }

                if (declaration.Origin.PendingDecision == PendingDecisions.None)
                {
                    outsideWithoutPendingLine++;
                }
            }

            report.AppendLine($"  fields indexed        : {bindings.Count + outside}");
            report.AppendLine($"  carrying origin tag   : {tagged + outside}");
            report.AppendLine($"  invented (no source)  : {invented + outsideInvented}");
            report.AppendLine($"  outside PrototypeConfig : {outside}");
            report.AppendLine($"  without an origin tag : {untagged.Count}");
            for (int i = 0; i < untagged.Count; i++)
            {
                report.AppendLine($"    - {untagged[i]}");
            }

            report.AppendLine($"    config rows expand every list entry ({bindings.Count} rows from "
                              + $"{configKeys.Count} declarations); rows outside the config are counted once per "
                              + "declared field.");
            report.AppendLine($"    of the {outside} outside: {outsideInvented} invented, "
                              + $"{outsideWithoutPendingLine} occupy no pending planning line.");

            Check(failures, "Every config field carries an origin tag", untagged.Count == 0, untagged.Count);
            Check(failures, "The index reaches past PrototypeConfig", outside > 0, outside);
            // Independent cross-check: the census scans types, the walk scans the live asset.
            // If the census ever misses an assembly, every field the walk found still has to
            // be in it - so this catches a silently narrowed scan.
            int knownToCensus = 0;
            foreach (string key in configKeys)
            {
                if (PrototypeOnlyCensus.Find(key) != null)
                {
                    knownToCensus++;
                }
            }

            Check(failures, "Census knows every field the config walk found",
                knownToCensus == configKeys.Count, $"{knownToCensus}/{configKeys.Count}");

            // Coverage: how much of that surface a live rebuild can actually show right now.
            // In an editor run nothing is spawned, so a large unbound count here is expected
            // and is reported rather than asserted. What must always bind is the part that
            // needs no scene at all.
            ConfigFieldIndexReport live = ConfigFieldIndex.BuildAll(config);
            int staticBound = 0;
            for (int i = 0; i < live.Bindings.Count; i++)
            {
                if (live.Bindings[i].IsStaticField)
                {
                    staticBound++;
                }
            }

            report.AppendLine("  live coverage in this context:");
            report.AppendLine($"    rows built            : {live.Bindings.Count}");
            report.AppendLine($"    bound outside config  : {live.BoundOutsideConfig} / {outside}");
            report.AppendLine($"    live owners with tags : {live.OwnersWithTaggedFields} "
                              + $"(of {live.OwnersScanned} loaded objects, {live.SingletonsActivated} tuning singletons)");
            report.AppendLine($"    unbound (no instance) : {live.UnboundCount}");
            for (int i = 0; i < live.Unbound.Count; i++)
            {
                report.AppendLine($"      - {live.Unbound[i]}");
            }

            Check(failures, "Every const/static tagged field binds without an instance",
                staticBound == PrototypeOnlyCensus.StaticCount, $"{staticBound}/{PrototypeOnlyCensus.StaticCount}");

            Debug.Log(report.ToString());

            if (failures.Count == 0)
            {
                Debug.Log("[Validate] PASS - all core checks green.");
            }
            else
            {
                for (int i = 0; i < failures.Count; i++)
                {
                    Debug.LogError("[Validate] FAIL - " + failures[i]);
                }
            }
        }

        private static void Check(List<string> failures, string label, bool condition, object actual)
        {
            if (!condition)
            {
                failures.Add($"{label} (actual: {actual})");
            }
        }
    }
}
