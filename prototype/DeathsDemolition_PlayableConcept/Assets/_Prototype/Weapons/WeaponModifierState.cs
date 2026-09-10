using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// The run's weapon build: one owned tier per upgrade branch.
    ///
    /// THIS IS THE ENTIRE SURFACE THE UPGRADE-CARD TASK NEEDS.
    /// A purchased card is one call:
    ///
    ///     WeaponRegistry.ActiveController.Modifiers.AddTier(UpgradeBranch.Piercing);
    ///
    /// and nothing else. The weapon re-reads the state on the next shot; there is no
    /// apply step, no rebuild, no event to wait for. <see cref="Changed"/> exists only
    /// so a HUD can redraw the build.
    ///
    /// REQ-059 lives here by omission: this object is read by
    /// <see cref="ClassicWeaponRuntime"/> and by nothing else. The Special Weapon never
    /// looks at it, so no card can reach it, and no future card can accidentally reach
    /// it either without someone deliberately adding a reference that does not exist.
    /// </summary>
    public sealed class WeaponModifierState
    {
        private const int BranchCount = 10;

        private readonly int[] _tiers = new int[BranchCount];

        /// <summary>Raised whenever any tier changes. HUD and build readout only.</summary>
        public event Action<WeaponModifierState> Changed;

        // ------------------------------------------------------------------ tiers

        public int GetTier(UpgradeBranch branch)
        {
            int index = (int)branch;
            return index >= 0 && index < BranchCount ? _tiers[index] : 0;
        }

        /// <summary>Set a tier outright. Clamped to the branch's MaxTier from the config.</summary>
        public void SetTier(UpgradeBranch branch, int tier, PrototypeConfig config = null)
        {
            int index = (int)branch;
            if (index < 0 || index >= BranchCount)
            {
                return;
            }

            int max = MaxTier(branch, config);
            int clamped = Mathf.Clamp(tier, 0, max);

            if (_tiers[index] == clamped)
            {
                return;
            }

            _tiers[index] = clamped;
            Changed?.Invoke(this);
        }

        /// <summary>Buy one tier of a branch. The normal call from a purchased card.</summary>
        public void AddTier(UpgradeBranch branch, int amount = 1, PrototypeConfig config = null)
        {
            SetTier(branch, GetTier(branch) + amount, config);
        }

        /// <summary>True when the branch is already at its ceiling and should stop being offered.</summary>
        public bool IsMaxed(UpgradeBranch branch, PrototypeConfig config = null)
        {
            return GetTier(branch) >= MaxTier(branch, config);
        }

        /// <summary>Wipe the build. Called when a run ends; gold and build do not persist.</summary>
        public void ResetForNewRun()
        {
            bool any = false;
            for (int i = 0; i < BranchCount; i++)
            {
                if (_tiers[i] != 0)
                {
                    _tiers[i] = 0;
                    any = true;
                }
            }

            if (any)
            {
                Changed?.Invoke(this);
            }
        }

        private static int MaxTier(UpgradeBranch branch, PrototypeConfig config)
        {
            PrototypeConfig c = config != null ? config : PrototypeConfig.Active;
            if (c == null)
            {
                return 99;
            }

            UpgradeBranchSettings settings = c.Economy.Find(branch);
            return settings != null ? Mathf.Max(0, settings.MaxTier) : 0;
        }

        // ------------------------------------------------------------------ resolved effects
        // Everything below turns owned tiers into the numbers a shot actually uses.
        // Read fresh on every shot: a card bought between two shots applies to the
        // second one with no plumbing.

        /// <summary>Extra enemies a bullet passes through. 0 with no Piercing.</summary>
        public int PierceTargets
        {
            get
            {
                WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
                return GetTier(UpgradeBranch.Piercing) * Mathf.Max(0, t.PierceTargetsPerTier);
            }
        }

        /// <summary>Damage retained after each pass-through.</summary>
        public float PierceDamageFalloff => WeaponUpgradeTuning.Active.PierceDamageFalloff;

        public bool HasRicochet => GetTier(UpgradeBranch.Ricochet) > 0;

        /// <summary>Probability that a landed hit bounces onward.</summary>
        public float RicochetChance
        {
            get
            {
                int tier = GetTier(UpgradeBranch.Ricochet);
                if (tier <= 0)
                {
                    return 0f;
                }

                WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
                return Mathf.Clamp01(t.RicochetBaseChance + t.RicochetChancePerTier * (tier - 1));
            }
        }

        /// <summary>How many times a bounce may chain onward.</summary>
        public int RicochetBounces
        {
            get
            {
                int tier = GetTier(UpgradeBranch.Ricochet);
                if (tier <= 0)
                {
                    return 0;
                }

                WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
                int per = Mathf.Max(1, t.RicochetTiersPerExtraBounce);
                return 1 + (tier - 1) / per;
            }
        }

        public float RicochetRadius => WeaponUpgradeTuning.Active.RicochetRadius;

        public float RicochetDamageScale => WeaponUpgradeTuning.Active.RicochetDamageScale;

        public bool HasExplosion => GetTier(UpgradeBranch.Explosion) > 0;

        /// <summary>Probability that a landed hit detonates.</summary>
        public float ExplosionChance
        {
            get
            {
                int tier = GetTier(UpgradeBranch.Explosion);
                if (tier <= 0)
                {
                    return 0f;
                }

                WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
                return Mathf.Clamp01(t.ExplosionBaseChance + t.ExplosionChancePerTier * (tier - 1));
            }
        }

        public float ExplosionRadius
        {
            get
            {
                int tier = GetTier(UpgradeBranch.Explosion);
                if (tier <= 0)
                {
                    return 0f;
                }

                WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
                return t.ExplosionBaseRadius + t.ExplosionRadiusPerTier * (tier - 1);
            }
        }

        /// <summary>Blast damage as a multiple of the hit that triggered it.</summary>
        public float ExplosionDamageScale
        {
            get
            {
                int tier = GetTier(UpgradeBranch.Explosion);
                if (tier <= 0)
                {
                    return 0f;
                }

                WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
                return t.ExplosionBaseDamageScale + t.ExplosionDamageScalePerTier * (tier - 1);
            }
        }

        public bool HasChain => GetTier(UpgradeBranch.Chain) > 0;

        public int ChainTargets
        {
            get
            {
                WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
                return GetTier(UpgradeBranch.Chain) * Mathf.Max(0, t.ChainTargetsPerTier);
            }
        }

        public float ChainRadius => WeaponUpgradeTuning.Active.ChainRadius;

        public float ChainDamageScale => WeaponUpgradeTuning.Active.ChainDamageScale;

        public float ChainDamageFalloff => WeaponUpgradeTuning.Active.ChainDamageFalloff;

        /// <summary>Multiplier on the weapon's base fire rate. 1 with no Fire Rate tiers.</summary>
        public float FireRateMultiplier
        {
            get
            {
                WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
                float value = 1f + t.FireRateBonusPerTier * GetTier(UpgradeBranch.FireRate);
                return Mathf.Min(value, Mathf.Max(1f, t.MaximumFireRateMultiplier));
            }
        }

        /// <summary>Multiplier on the weapon's base reload time. 1 with no Reload Speed tiers.</summary>
        public float ReloadTimeMultiplier
        {
            get
            {
                WeaponUpgradeTuning t = WeaponUpgradeTuning.Active;
                float value = 1f - t.ReloadSpeedBonusPerTier * GetTier(UpgradeBranch.ReloadSpeed);
                return Mathf.Max(t.MinimumReloadFraction, value);
            }
        }

        /// <summary>Short human-readable build identity, e.g. "P2 / C1 / FR3". For HUD and results.</summary>
        public string DescribeBuild()
        {
            var sb = new System.Text.StringBuilder(48);
            AppendIf(sb, "P", UpgradeBranch.Piercing);
            AppendIf(sb, "R", UpgradeBranch.Ricochet);
            AppendIf(sb, "X", UpgradeBranch.Explosion);
            AppendIf(sb, "C", UpgradeBranch.Chain);
            AppendIf(sb, "FR", UpgradeBranch.FireRate);
            AppendIf(sb, "RL", UpgradeBranch.ReloadSpeed);
            return sb.Length == 0 ? "Stock" : sb.ToString();
        }

        private void AppendIf(System.Text.StringBuilder sb, string prefix, UpgradeBranch branch)
        {
            int tier = GetTier(branch);
            if (tier <= 0)
            {
                return;
            }

            if (sb.Length > 0)
            {
                sb.Append(" / ");
            }

            sb.Append(prefix).Append(tier);
        }
    }
}
