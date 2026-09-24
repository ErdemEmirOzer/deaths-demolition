using System.Collections.Generic;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.DebugTools;
using DD.Prototype.Core.Pooling;
using DD.Prototype.Feedback;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// The Weapons tab of the F1 panel.
    ///
    /// Split out of the weapon itself so that no gameplay class carries debug plumbing,
    /// and so the panel survives the weapon being destroyed between runs. Everything it
    /// exposes is either a live config edit (which flows back through ApplyConfig like
    /// any other) or a direct call into a system that is already public.
    ///
    /// The readouts matter as much as the buttons: the crowd budget, the hit-stop counter
    /// and the stagger throttle are the only way to see that REQ-124, REQ-127 and
    /// REQ-128 are actually holding in a forty-enemy fight, rather than believing they do.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponDebugHooks : MonoBehaviour
    {
        private static WeaponDebugHooks s_instance;

        private readonly List<DebugPanelEntry> _entries = new List<DebugPanelEntry>(24);

        /// <summary>Create the hooks if they do not exist. Safe to call repeatedly.</summary>
        public static void EnsureExists()
        {
            if (s_instance != null)
            {
                return;
            }

            var go = new GameObject("[Weapon Debug Hooks]");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<WeaponDebugHooks>();
        }

        private void OnEnable()
        {
            s_instance = this;

            Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Weapons, "Trigger Special Weapon", TriggerSpecial, -100));
            Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Weapons, "End Special Weapon", EndSpecial, -99));

            Add(DebugPanelRegistry.AddToggle(
                DebugPanelTabs.Weapons, "Infinite Ammo",
                () => Controller != null && Controller.Classic.InfiniteAmmo,
                v =>
                {
                    if (Controller != null)
                    {
                        Controller.Classic.InfiniteAmmo = v;
                    }
                },
                -90));

            // WP-069 B2: one button per enabled Arsenal weapon (the selection screen still lists the v1 roster until S-09 R1-A).
            System.Collections.Generic.IReadOnlyList<DD.Prototype.Core.Weapons.WeaponDefinition> arsenal = DD.Prototype.Core.Weapons.Arsenal.All;
            int order = -89;
            for (int i = 0; i < arsenal.Count; i++)
            {
                DD.Prototype.Core.Weapons.WeaponDefinition d = arsenal[i];
                if (d == null || !d.enabled || d.slot != DD.Prototype.Core.Weapons.WeaponSlotKind.Classic) continue;
                ClassicWeaponId id = d.id;
                Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Weapons, "Equip: " + d.displayName + " (" + d.weaponId + ")",
                    () => Equip(id), order++));
            }

            if (arsenal.Count == 0)
            {
                Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Weapons, "Equip: Assault Rifle",
                    () => Equip(ClassicWeaponId.AssaultRifle), -80));
                Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Weapons, "Equip: Auto SMG",
                    () => Equip(ClassicWeaponId.AutoSmg), -79));
                Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Weapons, "Equip: Combat Shotgun",
                    () => Equip(ClassicWeaponId.CombatShotgun), -78));
            }

            Add(DebugPanelRegistry.AddSlider(
                DebugPanelTabs.Weapons, "Fire Rate (shots/s)",
                GetFireRate, SetFireRate, 0.5f, 30f, false, -70));

            Add(DebugPanelRegistry.AddSlider(
                DebugPanelTabs.Weapons, "Damage",
                GetDamage, SetDamage, 1f, 60f, false, -69));

            Add(DebugPanelRegistry.AddSlider(
                DebugPanelTabs.Weapons, "Camera Shake Intensity",
                () => CameraShakeBridge.IntensityScale,
                v => CameraShakeBridge.IntensityScale = v,
                0f, 2f, false, -68));

            // WP-062H: the whole feel layer on/off (A/B) — FeelConfig's runtime master switch, committed next frame.
            Add(DebugPanelRegistry.AddToggle(
                DebugPanelTabs.Weapons, "Game Feel (A/B)",
                () => FeelRuntime.EnabledAfterCommit,
                FeelRuntime.SetEnabled, -67));

            Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Weapons, "+1 Piercing",
                () => AddTier(UpgradeBranch.Piercing), -60));
            Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Weapons, "+1 Ricochet",
                () => AddTier(UpgradeBranch.Ricochet), -59));
            Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Weapons, "+1 Explosion",
                () => AddTier(UpgradeBranch.Explosion), -58));
            Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Weapons, "+1 Chain",
                () => AddTier(UpgradeBranch.Chain), -57));
            Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Weapons, "Clear Build",
                () => { if (Controller != null) { Controller.Modifiers.ResetForNewRun(); } }, -56));

            Add(DebugPanelRegistry.AddToggle(
                DebugPanelTabs.Weapons, "Damage Numbers",
                () => HitFeedbackDirector.Instance.ShowDamageNumbers,
                v => HitFeedbackDirector.Instance.ShowDamageNumbers = v, -50));

            Add(DebugPanelRegistry.AddToggle(
                DebugPanelTabs.Weapons, "Hit Markers",
                () => HitFeedbackDirector.Instance.ShowHitMarkers,
                v => HitFeedbackDirector.Instance.ShowHitMarkers = v, -49));

            Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Weapons, "Weapon", DescribeWeapon, -40));
            Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Weapons, "Build", DescribeBuild, -39));
            Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Weapons, "Special", DescribeSpecial, -38));
            Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Weapons, "Feel budget", DescribeBudget, -37));
            Add(DebugPanelRegistry.AddReadout(DebugPanelTabs.Weapons, "Effect pools", DescribePools, -36));

            Add(DebugPanelRegistry.AddButton(DebugPanelTabs.Weapons, "Reset feel counters", () =>
            {
                HitFeedbackDirector.Instance.Budget.ResetCounters();
                HitStopService.ResetCounters();
                StaggerGovernor.ResetCounters();
                AudioEvents.ResetCounters();
            }, -30));
        }

        private void OnDisable()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                DebugPanelRegistry.Remove(_entries[i]);
            }

            _entries.Clear();

            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        private void Add(DebugPanelEntry entry)
        {
            if (entry != null)
            {
                _entries.Add(entry);
            }
        }

        // ------------------------------------------------------------------ actions

        private static WeaponController Controller => WeaponRegistry.ActiveController;

        private static void TriggerSpecial()
        {
            SpecialWeaponDirector director = FindObjectOfType<SpecialWeaponDirector>();
            if (director != null)
            {
                director.ForceSpawn();
                return;
            }

            // No director in the scene: hand a weapon straight over so a Special can still
            // be felt. The spawn ceremony is skipped, which is fine for a debug path.
            if (Controller != null)
            {
                Controller.ActivateSpecialWeapon(NextDebugWeapon());
            }
        }

        /// <summary>
        /// Cycles the three weapons rather than rolling, so the debug button can reach all
        /// of them deterministically instead of leaving one untested behind a dice roll.
        /// </summary>
        private static SpecialWeaponId NextDebugWeapon()
        {
            s_debugWeapon = (SpecialWeaponId)(((int)s_debugWeapon + 1) % SpecialWeaponConfigSection.All.Length);
            return s_debugWeapon;
        }

        private static SpecialWeaponId s_debugWeapon = SpecialWeaponId.Namekuji;   // the first press gives Raiju

        private static void EndSpecial()
        {
            if (Controller != null)
            {
                Controller.DeactivateSpecialWeapon();
            }
        }

        private static void Equip(ClassicWeaponId id)
        {
            WeaponRegistry.SelectedWeapon = id;

            if (Controller != null)
            {
                Controller.EquipClassic(id);
            }
        }

        private static void AddTier(UpgradeBranch branch)
        {
            if (Controller != null)
            {
                Controller.Modifiers.AddTier(branch);
            }
        }

        // ------------------------------------------------------------------ config edits

        private static ClassicWeaponSettings CurrentSettings()
        {
            PrototypeConfig config = PrototypeConfig.Active;
            if (config == null)
            {
                return null;
            }

            ClassicWeaponId id = Controller != null ? Controller.Classic.Id : WeaponRegistry.SelectedWeapon;
            return config.Weapons.Find(id);
        }

        private static float GetFireRate()
        {
            ClassicWeaponSettings settings = CurrentSettings();
            return settings != null ? settings.FireRate : 1f;
        }

        private static void SetFireRate(float value)
        {
            ClassicWeaponSettings settings = CurrentSettings();
            if (settings == null)
            {
                return;
            }

            settings.FireRate = Mathf.Max(0.1f, value);
            PrototypeConfig.Active.NotifyChanged();
        }

        private static float GetDamage()
        {
            ClassicWeaponSettings settings = CurrentSettings();
            return settings != null ? settings.Damage : 1f;
        }

        private static void SetDamage(float value)
        {
            ClassicWeaponSettings settings = CurrentSettings();
            if (settings == null)
            {
                return;
            }

            settings.Damage = Mathf.Max(0.1f, value);
            PrototypeConfig.Active.NotifyChanged();
        }

        // ------------------------------------------------------------------ readouts

        private static string DescribeWeapon()
        {
            WeaponController controller = Controller;
            if (controller == null)
            {
                return "no armed player";
            }

            ClassicWeaponRuntime c = controller.Classic;
            string ammo = c.InfiniteAmmo ? "INF" : $"{c.Ammo}/{c.MagazineSize}";
            string reload = c.IsReloading ? $" reloading {c.ReloadRemaining:0.00}s" : "";
            string parked = controller.SpecialWeaponActive ? " [parked]" : "";

            return $"{c.DisplayName} {ammo} @ {c.EffectiveFireRate:0.0}/s{reload}{parked}";
        }

        private static string DescribeBuild()
        {
            WeaponController controller = Controller;
            return controller != null ? controller.Modifiers.DescribeBuild() : "-";
        }

        private static string DescribeSpecial()
        {
            SpecialWeaponHudState state = WeaponEvents.Special;

            switch (state.Phase)
            {
                case SpecialWeaponPhase.OnGround:
                    // The HUD no longer shows this number (G-21). The panel still does:
                    // a diagnostic surface that lies to match the HUD is worthless.
                    return $"{state.WeaponId} on ground {state.GroundRemaining:0.0}s /"
                           + $" {state.GroundDuration:0.0}s"
                           + $" | blink {GroundBlinkClock.FrequencyHz(state.GroundProgress01):0.00} Hz";

                case SpecialWeaponPhase.Active:
                    return $"ACTIVE {state.WeaponId} {state.ActiveRemaining:0.0}s /"
                           + $" {state.ActiveDuration:0.0}s"
                           + DescribeSpecialField()
                           + (state.FinalSecondsWarning ? "  <final>" : "");

                default:
                    SpecialWeaponDirector director = FindObjectOfType<SpecialWeaponDirector>();
                    if (director == null)
                    {
                        return "none (no director)";
                    }

                    return director.State == SpecialWeaponDirector.SpecialWeaponState.Scheduled
                        ? $"scheduled at {director.ScheduledProgress:P0} round progress"
                        : $"none ({director.State}, {director.SpawnsThisRound} this round)";
            }
        }

        /// <summary>Whatever the weapon in hand has out on the map right now.</summary>
        private static string DescribeSpecialField()
        {
            WeaponController controller = Controller;
            if (controller == null || !controller.SpecialWeaponActive)
            {
                return string.Empty;
            }

            SpecialWeaponRuntime s = controller.Special;
            string ammo = $" | {s.Ammo}/{s.MagazineSize} +{s.ReserveAmmo}" + (s.IsReloading ? " reloading" : string.Empty);
            return controller.Special.Id == SpecialWeaponId.Raiju
                ? ammo + $" | chains {SpecialImpacts.ActiveChains} hops {SpecialImpacts.HopsApplied} kills {SpecialImpacts.LethalApplied} self {SpecialImpacts.SelfShocks}"
                : ammo + $" | gel hits {SpecialImpacts.GelHits}";
        }

        private static string DescribeBudget()
        {
            FeedbackBudget budget = HitFeedbackDirector.Instance.Budget;

            return $"dropped fx {budget.TotalSuppressed}"
                   + $" | blood {budget.SuppressedFor(FeedbackCategory.Blood)}"
                   + $" impact {budget.SuppressedFor(FeedbackCategory.Impact)}"
                   + $" special {budget.SuppressedFor(FeedbackCategory.SpecialWeapon)}"
                   + $" | hitstop {HitStopService.Count} (refused {HitStopService.Refused})"
                   + $" | stagger throttled {StaggerGovernor.ThrottledCount}"
                   + $" | audio dropped {AudioEvents.SuppressedCount}";
        }

        private static string DescribePools()
        {
            int spawned = 0;
            int total = 0;

            IReadOnlyList<IPrototypePool> pools = PoolService.Pools;
            for (int i = 0; i < pools.Count; i++)
            {
                spawned += pools[i].CountSpawned;
                total += pools[i].CountSpawned + pools[i].CountAvailable;
            }

            return $"{spawned} live / {total} instances across {pools.Count} pools"
                   + $" | collider cache {DamageTargetResolver.CachedColliders}";
        }
    }
}
