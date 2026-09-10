using DD.Prototype.Core.DebugTools;
using DD.Prototype.Core.InputAbstraction;
using UnityEngine;

namespace DD.Prototype.PlayerSystem
{
    /// <summary>
    /// The player's rows in the F1 panel. Registered on enable, removed on disable, so
    /// the panel never shows a control for a player that no longer exists.
    ///
    /// Nothing here is a gameplay feature. The invulnerability toggle in particular is
    /// labelled as a diagnosis aid because REQ-044 forbids damage immunity in the game
    /// itself.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerDebugHooks : MonoBehaviour
    {
        private PlayerHealth _health;
        private PlayerMotor _motor;
        private PlayerAim _aim;

        private DebugPanelButton _heal;
        private DebugPanelButton _hurt;
        private DebugPanelButton _kill;
        private DebugPanelToggle _invulnerable;
        private DebugPanelReadout _healthReadout;
        private DebugPanelReadout _positionReadout;
        private DebugPanelReadout _aimReadout;
        private DebugPanelReadout _upgradeReadout;

        private void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _motor = GetComponent<PlayerMotor>();
            _aim = GetComponent<PlayerAim>();
        }

        private void OnEnable()
        {
            _heal = DebugPanelRegistry.AddButton(
                DebugPanelTabs.Misc, "Heal Player", HealPlayer, -80);

            _hurt = DebugPanelRegistry.AddButton(
                DebugPanelTabs.Misc, "Damage Player 25", () => DamagePlayer(25f), -79);

            _kill = DebugPanelRegistry.AddButton(
                DebugPanelTabs.Misc, "Kill Player", () => DamagePlayer(float.MaxValue), -78);

            _invulnerable = DebugPanelRegistry.AddToggle(
                DebugPanelTabs.Misc,
                "Toggle Invulnerable (diagnosis only)",
                () => _health != null && _health.DiagnosisInvulnerable,
                v => { if (_health != null) _health.DiagnosisInvulnerable = v; },
                -77);

            _healthReadout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Misc, "Player health", DescribeHealth, -76);

            _positionReadout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Misc, "Player position", DescribePosition, -75);

            _aimReadout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Misc, "Player aim", DescribeAim, -74);

            // The four player-side upgrade branches were dead code once. This row is how
            // anyone checks in one glance that a bought card actually moved a number.
            _upgradeReadout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Misc, "Player upgrades", DescribeUpgrades, -73);
        }

        private void OnDisable()
        {
            DebugPanelRegistry.Remove(_heal);
            DebugPanelRegistry.Remove(_hurt);
            DebugPanelRegistry.Remove(_kill);
            DebugPanelRegistry.Remove(_invulnerable);
            DebugPanelRegistry.Remove(_healthReadout);
            DebugPanelRegistry.Remove(_positionReadout);
            DebugPanelRegistry.Remove(_aimReadout);
            DebugPanelRegistry.Remove(_upgradeReadout);

            _upgradeReadout = null;
            _heal = null;
            _hurt = null;
            _kill = null;
            _invulnerable = null;
            _healthReadout = null;
            _positionReadout = null;
            _aimReadout = null;
        }

        private void HealPlayer()
        {
            if (_health != null)
            {
                _health.HealToFull();
            }
        }

        private void DamagePlayer(float amount)
        {
            if (_health == null)
            {
                return;
            }

            bool wasInvulnerable = _health.DiagnosisInvulnerable;
            _health.DiagnosisInvulnerable = false;

            Vector3 behind = transform.position - transform.forward * 2f;
            _health.ApplyDamage(new PlayerDamageRequest(amount, transform.position, behind, "DebugPanel"));

            _health.DiagnosisInvulnerable = wasInvulnerable;
        }

        private string DescribeHealth()
        {
            if (_health == null)
            {
                return "no health component";
            }

            if (_health.IsDead)
            {
                return "DEAD";
            }

            string regen = _health.IsRegenerating
                ? "regenerating"
                : "regen in " + _health.SecondsUntilRegen.ToString("0.0") + " s";

            return string.Format("{0:0.0} / {1:0}   {2}{3}",
                _health.Current, _health.Max, regen,
                _health.DiagnosisInvulnerable ? "   [INVULNERABLE - diagnosis]" : string.Empty);
        }

        private string DescribePosition()
        {
            Vector3 p = transform.position;
            string speed = _motor != null
                ? string.Format("  speed {0:0.00} m/s", _motor.PlanarVelocity.magnitude)
                : string.Empty;

            return string.Format("({0:0.0}, {1:0.0}, {2:0.0}){3}", p.x, p.y, p.z, speed);
        }

        /// <summary>
        /// The player-side build and what it is worth right now, in resolved numbers rather
        /// than in tiers, so a card that claims +6 % speed can be checked against the motor.
        /// </summary>
        private string DescribeUpgrades()
        {
            if (_health == null)
            {
                return "no health component";
            }

            string speed = _motor != null
                ? string.Format("{0:0.00} m/s (x{1:0.00})", _motor.MaxSpeed, _motor.MoveSpeedUpgradeMultiplier)
                : "no motor";

            return string.Format("{0}   speed {1}   max HP {2:0}   regen {3:0.0} s / {4:0.0} HP/s",
                _health.UpgradeBuildDescription,
                speed,
                _health.Max,
                _health.RegenDelaySeconds,
                _health.RegenRatePerSecond);
        }

        private string DescribeAim()
        {
            if (_aim == null)
            {
                return "no aim component";
            }

            Vector3 point = _aim.AimWorldPoint;
            AimSourceKind kind = _aim.AimKind;

            return string.Format("{0}  target ({1:0.0}, {2:0.0})  turn error {3:0.0} deg",
                kind, point.x, point.z, _aim.FacingErrorDegrees);
        }
    }
}
