using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Weapons;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// The player's window onto the run's purchased upgrade tiers.
    ///
    /// WHY THIS EXISTS
    /// ---------------
    /// <see cref="WeaponModifierState"/> holds a tier for all ten branches, but it lives on
    /// the <see cref="WeaponController"/>, which is created and destroyed independently of
    /// the player rig and may register after it. A player component that read
    /// <c>WeaponRegistry.ActiveController.Modifiers</c> once in Awake would cache a null and
    /// never recover, which is exactly how four upgrade cards end up taking gold and
    /// changing nothing.
    ///
    /// So this object owns the whole subscription problem and hands its owner one thing to
    /// react to:
    ///
    ///     private readonly PlayerUpgradeBinding _upgrades = new PlayerUpgradeBinding();
    ///
    ///     protected override void OnEnable()
    ///     {
    ///         _upgrades.Changed += OnUpgradesChanged;   // tier bought, or weapon swapped
    ///         _upgrades.Bind();                          // before base, so the first
    ///         base.OnEnable();                           // ApplyConfig already sees tiers
    ///     }
    ///
    /// <see cref="Changed"/> is raised when a tier is bought, when the run resets the build,
    /// and when the armed player appears or disappears - every case where a cached
    /// multiplier has gone stale. The owner's answer is always the same: re-run ApplyConfig.
    ///
    /// It never writes a tier. Buying stays the shop's single call to
    /// <c>WeaponModifierState.AddTier</c>.
    /// </summary>
    public sealed class PlayerUpgradeBinding
    {
        private WeaponModifierState _state;
        private bool _bound;

        /// <summary>A tier changed, or the state this binding reads was swapped.</summary>
        public event Action Changed;

        /// <summary>True while attached to a live modifier state. Diagnosis only.</summary>
        public bool HasState => _state != null;

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Call from OnEnable, before base.OnEnable, so the first ApplyConfig sees the tiers.</summary>
        public void Bind()
        {
            if (_bound)
            {
                return;
            }

            _bound = true;
            WeaponRegistry.ActiveChanged += OnActiveControllerChanged;
            Attach(WeaponRegistry.ActiveController);
        }

        /// <summary>Call from OnDisable. Leaves no subscription behind.</summary>
        public void Unbind()
        {
            if (!_bound)
            {
                return;
            }

            _bound = false;
            WeaponRegistry.ActiveChanged -= OnActiveControllerChanged;
            Detach();
        }

        private void OnActiveControllerChanged(WeaponController controller)
        {
            Detach();
            Attach(controller);

            // A new controller means a new build - even an empty one, which is still a
            // change from whatever the owner last cached.
            Raise();
        }

        private void Attach(WeaponController controller)
        {
            _state = controller != null ? controller.Modifiers : null;

            if (_state != null)
            {
                _state.Changed += OnModifiersChanged;
            }
        }

        private void Detach()
        {
            if (_state == null)
            {
                return;
            }

            _state.Changed -= OnModifiersChanged;
            _state = null;
        }

        private void OnModifiersChanged(WeaponModifierState state)
        {
            Raise();
        }

        private void Raise()
        {
            Action handler = Changed;
            if (handler != null)
            {
                handler.Invoke();
            }
        }

        // ------------------------------------------------------------------ reads

        /// <summary>Owned tier of a branch. 0 outside a run, which is the correct answer.</summary>
        public int Tier(UpgradeBranch branch)
        {
            return _state != null ? _state.GetTier(branch) : 0;
        }

        /// <summary>Multiplier on the configured top speed.</summary>
        public float MoveSpeedMultiplier =>
            PlayerUpgradeTuning.Active.MoveSpeedMultiplier(Tier(UpgradeBranch.MovementSpeed));

        /// <summary>Multiplier on the configured maximum health.</summary>
        public float MaxHealthMultiplier =>
            PlayerUpgradeTuning.Active.MaxHealthMultiplier(Tier(UpgradeBranch.MaximumHealth));

        /// <summary>Multiplier on the configured regeneration delay. Below 1 once bought.</summary>
        public float RegenDelayMultiplier =>
            PlayerUpgradeTuning.Active.RegenDelayMultiplier(Tier(UpgradeBranch.RegenerationDelay));

        /// <summary>Multiplier on the configured regeneration rate.</summary>
        public float RegenRateMultiplier =>
            PlayerUpgradeTuning.Active.RegenRateMultiplier(Tier(UpgradeBranch.RegenerationRate));

        /// <summary>Short readout of the player-side half of the build, e.g. "MS2 / HP1". F1 only.</summary>
        public string DescribeBuild()
        {
            var sb = new System.Text.StringBuilder(32);
            Append(sb, "MS", UpgradeBranch.MovementSpeed);
            Append(sb, "HP", UpgradeBranch.MaximumHealth);
            Append(sb, "RD", UpgradeBranch.RegenerationDelay);
            Append(sb, "RR", UpgradeBranch.RegenerationRate);
            return sb.Length == 0 ? "Stock" : sb.ToString();
        }

        private void Append(System.Text.StringBuilder sb, string prefix, UpgradeBranch branch)
        {
            int tier = Tier(branch);
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
