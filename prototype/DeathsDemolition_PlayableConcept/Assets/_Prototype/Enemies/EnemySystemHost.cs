using System.Collections.Generic;
using UnityEngine;
using DD.Prototype.Core.Config;
using DD.Prototype.Core;
using DD.Prototype.Core.DebugTools;

namespace DD.Prototype.Enemies
{
    /// <summary>
    /// The enemy family's single config-bound object and its F1 panel surface.
    ///
    /// WHY THIS EXISTS
    /// ZombieEnemy cannot derive from PrototypeConfigBehaviour because pooling already
    /// spends its base class on PooledBehaviour. This host takes that role for the whole
    /// family: it is a PrototypeConfigBehaviour, it reads config only inside ApplyConfig,
    /// and it fans the change out to the factory, the effect pools and every enemy already
    /// on the map. A value edited in the F1 panel mid-round therefore reaches a zombie that
    /// is already walking towards the player, which is the live-config contract.
    ///
    /// It creates itself, so no scene needs to remember to contain it and four parallel
    /// tasks never have to merge the same scene file.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemySystemHost : PrototypeConfigBehaviour
    {
        private const string HostName = "[Enemy System]";

        private static EnemySystemHost s_instance;

        private readonly List<DebugPanelEntry> _debugEntries = new List<DebugPanelEntry>(12);

        /// <summary>PROTOTYPE-ONLY / INVENTED: ring radius used by the debug spawn buttons.</summary>
        [PrototypeOnly(PendingDecisions.None,
            "Metres from the player where an F1-panel debug spawn is dropped. Debug affordance " +
            "only - the spawn director never uses it.", true)]
        private const float DebugSpawnRadius = 7f;

        // ------------------------------------------------ debug crowd mix (F1 panel only)
        // WP-010: "Fill To Safety Ceiling" draws its tiers from the run's single tier
        // authority (the director's selector, current round, through
        // EnemyTierAuthorityBridge) - the former private cut table was a second, unrelated
        // tier chooser. At round 1 the authority yields Standard only; to measure a mixed
        // ~40 crowd, skip to a later round first.

        public static EnemySystemHost Instance => s_instance;

        /// <summary>Create the host if it does not exist yet. Idempotent.</summary>
        public static EnemySystemHost EnsureExists()
        {
            if (s_instance != null)
            {
                return s_instance;
            }

            var go = new GameObject(HostName);
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<EnemySystemHost>();
            return s_instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            s_instance = null;
            EnsureExists();
        }

        protected override void OnEnable()
        {
            s_instance = this;
            base.OnEnable();   // calls ApplyConfig
            RegisterDebugPanel();
        }

        protected override void OnDisable()
        {
            UnregisterDebugPanel();
            base.OnDisable();

            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        /// <summary>
        /// The family's single config read point. Everything derived from config is
        /// recomputed here and pushed down.
        /// </summary>
        protected override void ApplyConfig(PrototypeConfig config)
        {
            if (config == null)
            {
                return;
            }

            EnemyEffects.ApplyConfig(config);
            EnemyFactory.ApplyConfig(config);
        }

        // ================================================================= debug panel

        private void RegisterDebugPanel()
        {
            const string tab = DebugPanelTabs.Enemies;

            Add(DebugPanelRegistry.AddButton(tab, "Clear All Enemies",
                () => Debug.Log($"[Enemies] Cleared {EnemyFactory.ClearAll()} enemies (no gold, no kill credit)."), 0));

            Add(DebugPanelRegistry.AddButton(tab, "Kill All Enemies",
                () => Debug.Log($"[Enemies] Killed {EnemyFactory.KillAll()} enemies through the normal death path."), 1));

            Add(DebugPanelRegistry.AddButton(tab, "Spawn Standard",
                () => DebugSpawn(EnemyTier.Standard), 10));
            Add(DebugPanelRegistry.AddButton(tab, "Spawn Runner",
                () => DebugSpawn(EnemyTier.Special), 11));
            Add(DebugPanelRegistry.AddButton(tab, "Spawn Guard",
                () => DebugSpawn(EnemyTier.Elite), 12));
            Add(DebugPanelRegistry.AddButton(tab, "Spawn Executioner",
                () => DebugSpawn(EnemyTier.Ultra), 13));

            Add(DebugPanelRegistry.AddButton(tab, "Fill To Safety Ceiling",
                FillToCeiling, 14));

            Add(DebugPanelRegistry.AddButton(tab, "Break All Armor",
                () => Debug.Log($"[Enemies] Broke {EnemyFactory.BreakAllArmor()} armour pieces."), 20));

            // Runs the REQ-082..REQ-090 assertions against the real damage model and logs
            // every violation. This is the observable form of acceptance criterion 8 and 9.
            Add(DebugPanelRegistry.AddButton(tab, "Verify Damage Model",
                () => DD.Prototype.Damage.RegionalDamageProof.VerifyAndDescribe(), 21));

            Add(DebugPanelRegistry.AddReadout(tab, "Crowd", EnemyFactory.DescribeCrowd, 30));
            Add(DebugPanelRegistry.AddReadout(tab, "Tier schedule", DescribeTierSchedule, 31));
            Add(DebugPanelRegistry.AddReadout(tab, "FX budget", EnemyEffects.DescribeBudget, 32));
            Add(DebugPanelRegistry.AddReadout(tab, "Armour detail", DescribeArmourDetail, 33));
        }

        private void Add(DebugPanelEntry entry)
        {
            if (entry != null)
            {
                _debugEntries.Add(entry);
            }
        }

        private void UnregisterDebugPanel()
        {
            for (int i = 0; i < _debugEntries.Count; i++)
            {
                DebugPanelRegistry.Remove(_debugEntries[i]);
            }

            _debugEntries.Clear();
        }

        // ------------------------------------------------------------------ debug acts

        private static string DescribeTierSchedule()
        {
            return EnemyTierAuthorityBridge.Describe();
        }

        private void DebugSpawn(EnemyTier tier)
        {
            int round = EnemyTierAuthorityBridge.CurrentRound();
            Vector3 origin = EnemyTargeting.PlayerPositionOr(Vector3.zero);

            float angle = Random.value * Mathf.PI * 2f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * DebugSpawnRadius;
            Vector3 position = origin + offset;

            Quaternion rotation = Quaternion.LookRotation(-offset.normalized, Vector3.up);

            ZombieEnemy enemy = EnemyFactory.Spawn(tier, round, position, rotation);
            if (enemy == null)
            {
                Debug.LogWarning($"[Enemies] Spawn refused: safety ceiling of {EnemyFactory.Capacity} reached.");
            }
        }

        /// <summary>
        /// Force the crowd up to the absolute technical safety ceiling so the ~40-enemy
        /// performance target can actually be measured - it does not occur in natural play,
        /// where the target-active formula tops out at 32.
        /// </summary>
        private void FillToCeiling()
        {
            int added = 0;
            int guard = 0;

            while (!EnemyFactory.IsAtCeiling && guard++ < 200)
            {
                int before = EnemyFactory.SpawnedCount;
                DebugSpawn(PickFillTier());
                if (EnemyFactory.SpawnedCount == before)
                {
                    break;
                }

                added++;
            }

            Debug.Log($"[Enemies] Filled to {EnemyFactory.SpawnedCount}/{EnemyFactory.Capacity} (+{added}).");
        }

        private static EnemyTier PickFillTier()
        {
            return EnemyTierAuthorityBridge.DrawWeighted(PrototypeConfig.Active);
        }

        private static string DescribeArmourDetail()
        {
            IReadOnlyList<ZombieEnemy> spawned = EnemyFactory.Spawned;
            if (spawned.Count == 0)
            {
                return "no enemies";
            }

            // Only the first few: the panel is a readout, not a spreadsheet.
            var builder = new System.Text.StringBuilder(160);
            int shown = 0;
            for (int i = 0; i < spawned.Count && shown < 5; i++)
            {
                if (spawned[i] == null || !spawned[i].IsAlive)
                {
                    continue;
                }

                if (shown > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(spawned[i].DescribeArmor());
                shown++;
            }

            if (shown == 0)
            {
                return "no living enemies";
            }

            return builder.ToString();
        }
    }
}
