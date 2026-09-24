using System;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using UnityEngine;

namespace DD.Prototype.UI
{
    /// <summary>Which HUD readouts changed in the last Apply* call. Bit flags.</summary>
    [Flags]
    public enum RunHudField
    {
        None = 0,
        Round = 1 << 0,
        Enemies = 1 << 1,
        Gold = 1 << 2,
        Health = 1 << 3,
        HealthState = 1 << 4,
        WeaponName = 1 << 5,
        Build = 1 << 6,
        Ammo = 1 << 7,
        Reload = 1 << 8,
        Suspended = 1 << 9,

        /// <summary>Bar fill or low-health colour only; the text did not change.</summary>
        HealthBar = 1 << 10
    }

    /// <summary>The reload readout's three states, decided here so the view only styles them.</summary>
    public enum RunHudReloadState
    {
        Ready = 0,
        Reloading = 1,
        MagazineEmpty = 2
    }

    /// <summary>Tone of the small status line under the health bar.</summary>
    public enum RunHudTone
    {
        Neutral = 0,
        Positive = 1,
        Danger = 2
    }

    /// <summary>
    /// The HUD's presenter (WP-005). Turns authoritative snapshots into the strings and
    /// numbers the HUD draws, and remembers what it last produced so a value that did
    /// not change costs nothing.
    ///
    /// WHY THIS EXISTS
    /// The HUD used to format every readout every frame - Round.ToString(), the enemy
    /// breakdown, the ammo line - which is an allocation per frame per label whether or
    /// not anything moved. Snapshots are still polled (that is the contract RunEvents and
    /// WeaponEvents ask for), but the formatting now happens only on the frame a number
    /// changes, and the view applies only the fields flagged in the returned mask. On a
    /// quiet frame every Apply* returns <see cref="RunHudField.None"/> and allocates 0 B.
    ///
    /// It has no Unity object in it, so every rule - what "—" means, when the health is
    /// low, what the reload line says - is provable in EditMode.
    /// </summary>
    public sealed class RunHudViewModel
    {
        /// <summary>What a readout shows before the run has published anything.</summary>
        public const string Placeholder = "—";

        // ---- round / enemies
        private int _round = int.MinValue;
        private int _enemiesRemaining = int.MinValue;
        private int _poolRemaining = int.MinValue;
        private int _alive = int.MinValue;
        private bool _poolExhausted;

        public string RoundText { get; private set; } = Placeholder;
        public string EnemiesText { get; private set; } = Placeholder;
        public string EnemiesBreakdown { get; private set; } = "waiting for the round to start";

        // ---- gold
        private int _gold = int.MinValue;
        public string GoldText { get; private set; } = "0";

        /// <summary>WP-068 slice 8 (E12): the balance as a number, for the rolling counter (0 before the first value).</summary>
        public int Gold
        {
            get { return _gold == int.MinValue ? 0 : _gold; }
        }

        // ---- health
        private int _healthCurrent = int.MinValue;
        private int _healthMax = int.MinValue;
        private bool _healthKnown;
        private int _regenTenths = int.MinValue;

        // WP-059D: the hurt path allocated nothing but these two strings — "cur / max" on every whole point a hit
        // (or regen) moves, and "recovery in X.Xs" every tenth the countdown moves (polled per frame). Built once
        // per value and reused: the "cur / max" table in full when the max is first seen (run bind / an upgrade,
        // never mid-fight), the countdown lazily per tenth for the whole session. Same text as before.
        private const int HealthTextCacheMax = 1000;
        private const int RegenTextCacheTenths = 600;
        private string[] _healthTexts;
        private int _healthTextsMax = int.MinValue;
        private static readonly string[] s_regenTexts = new string[RegenTextCacheTenths + 1];
        private bool _dead;
        private bool _regenerating;

        public float HealthNormalized { get; private set; }
        public bool HealthLow { get; private set; }
        public bool HealthKnown { get { return _healthKnown; } }
        public string HealthText { get; private set; } = Placeholder;
        public string HealthStateText { get; private set; } = string.Empty;
        public RunHudTone HealthStateTone { get; private set; } = RunHudTone.Neutral;

        // ---- classic weapon
        private string _weaponSource;
        private string _buildSource;
        private int _ammo = int.MinValue;
        private int _magazine = int.MinValue;
        private RunHudReloadState _reload = (RunHudReloadState)(-1);
        private bool _suspended;

        public string WeaponName { get; private set; } = Placeholder;
        public string BuildText { get; private set; } = "no upgrades";
        public string AmmoText { get; private set; } = Placeholder;
        public RunHudReloadState ReloadState { get; private set; } = RunHudReloadState.Ready;
        public float ReloadProgress01 { get; private set; } = 1f;
        public bool Suspended { get { return _suspended; } }

        /// <summary>WP-068 slice 8 (E11): the magazine as numbers - "24" NumericXL over "/ 30", the tick ladder. 0 while unknown.</summary>
        public int Ammo
        {
            get { return _ammo < 0 ? 0 : _ammo; }
        }

        public int Magazine
        {
            get { return _magazine < 0 ? 0 : _magazine; }
        }

        /// <summary>Pellets per shot (&gt; 1 → the ladder draws pellet pips).</summary>
        public int PelletCount { get; private set; }

        /// <summary>E11: at or under this share of the magazine (and not empty) the panel says LOW.</summary>
        public const float LowAmmoFraction = 0.2f;

        /// <summary>E11: over this magazine size the ladder is not drawn (the number carries it).</summary>
        public const int LadderMaxRounds = 40;

        public bool AmmoLow
        {
            get { return _magazine > 0 && _ammo > 0 && _ammo <= Mathf.FloorToInt(_magazine * LowAmmoFraction); }
        }

        /// <summary>Longest weapon name the block can hold before it is clipped.</summary>
        public const int WeaponNameMaxCharacters = 26;

        /// <summary>Longest build line the block can hold before it is clipped.</summary>
        public const int BuildMaxCharacters = 34;

        // ================================================================ round state

        /// <summary>Round, enemies and gold from the director's per-frame snapshot.</summary>
        public RunHudField ApplyRun(in RunHudState state)
        {
            RunHudField changed = RunHudField.None;

            if (state.Round != _round)
            {
                _round = state.Round;
                RoundText = _round > 0 ? _round.ToString() : Placeholder;
                changed |= RunHudField.Round;
            }

            bool breakdownChanged = state.PoolRemaining != _poolRemaining ||
                                    state.AliveEnemies != _alive ||
                                    state.PoolExhausted != _poolExhausted ||
                                    (changed & RunHudField.Round) != 0;

            if (state.EnemiesRemaining != _enemiesRemaining || (changed & RunHudField.Round) != 0)
            {
                _enemiesRemaining = state.EnemiesRemaining;
                EnemiesText = _round > 0 ? _enemiesRemaining.ToString() : Placeholder;
                changed |= RunHudField.Enemies;
            }

            if (breakdownChanged)
            {
                _poolRemaining = state.PoolRemaining;
                _alive = state.AliveEnemies;
                _poolExhausted = state.PoolExhausted;

                if (_round <= 0)
                {
                    // The run has not published a state yet: the HUD says so instead of
                    // showing a confident zero.
                    EnemiesBreakdown = "waiting for the round to start";
                }
                else if (_poolExhausted)
                {
                    EnemiesBreakdown = "pool empty  ·  " + _alive + " still on the map";
                }
                else
                {
                    EnemiesBreakdown = _poolRemaining + " left to arrive  ·  " + _alive + " on the map";
                }

                changed |= RunHudField.Enemies;
            }

            changed |= ApplyGold(state.Gold);
            return changed;
        }

        /// <summary>
        /// Gold on its own, so the wallet's change event can land between director
        /// frames and the counter still moves on the frame the orb was taken.
        /// </summary>
        public RunHudField ApplyGold(int balance)
        {
            if (balance == _gold)
            {
                return RunHudField.None;
            }

            _gold = balance;
            GoldText = balance.ToString();
            return RunHudField.Gold;
        }

        // ==================================================================== health

        /// <summary>
        /// Health from the player's own snapshot. <paramref name="lowFraction"/> is the
        /// ONE threshold the bar, the readout and the damage edge share.
        /// </summary>
        public RunHudField ApplyHealth(in PlayerHealthSnapshot snapshot, float lowFraction)
        {
            RunHudField changed = RunHudField.None;

            int current = Mathf.CeilToInt(Mathf.Max(0f, snapshot.Current));
            int max = Mathf.RoundToInt(snapshot.Max);

            float normalized = snapshot.Normalized;
            bool low = normalized <= lowFraction;

            // The bar moves continuously while health regenerates; the text only moves
            // when a whole point does. Two flags, so the regen tick never re-formats.
            if (!_healthKnown || !Mathf.Approximately(normalized, HealthNormalized) || low != HealthLow)
            {
                HealthNormalized = normalized;
                HealthLow = low;
                changed |= RunHudField.HealthBar;
            }

            if (!_healthKnown || current != _healthCurrent || max != _healthMax)
            {
                _healthKnown = true;
                _healthCurrent = current;
                _healthMax = max;
                HealthText = HealthTextFor(current, max);
                changed |= RunHudField.Health;
            }

            // The status line changes far less often than the number, and its regen
            // countdown is shown to a tenth, so it is compared at that resolution.
            int regenTenths = snapshot.SecondsUntilRegen > 0f ? Mathf.CeilToInt(snapshot.SecondsUntilRegen * 10f) : 0;

            if (snapshot.IsDead != _dead || snapshot.IsRegenerating != _regenerating || regenTenths != _regenTenths)
            {
                _dead = snapshot.IsDead;
                _regenerating = snapshot.IsRegenerating;
                _regenTenths = regenTenths;

                if (_dead)
                {
                    HealthStateText = "DOWN";
                    HealthStateTone = RunHudTone.Danger;
                }
                else if (_regenerating)
                {
                    HealthStateText = "RECOVERING";
                    HealthStateTone = RunHudTone.Positive;
                }
                else if (regenTenths > 0)
                {
                    HealthStateText = RegenTextFor(regenTenths);
                    HealthStateTone = RunHudTone.Neutral;
                }
                else
                {
                    HealthStateText = string.Empty;
                    HealthStateTone = RunHudTone.Neutral;
                }

                changed |= RunHudField.HealthState;
            }

            return changed;
        }

        /// <summary>WP-059D: "cur / max" from the per-max table (built when the max is first seen); outside the table, formatted as before.</summary>
        private string HealthTextFor(int current, int max)
        {
            if (max < 0 || max > HealthTextCacheMax || current < 0 || current > max)
            {
                return current + " / " + max;
            }

            if (_healthTexts == null || _healthTextsMax != max)
            {
                _healthTexts = new string[max + 1];
                for (int i = 0; i <= max; i++)
                {
                    _healthTexts[i] = i + " / " + max;
                }

                _healthTextsMax = max;
            }

            return _healthTexts[current];
        }

        /// <summary>WP-059D: "recovery in X.Xs", cached per tenth for the session (first use of a tenth formats it).</summary>
        private static string RegenTextFor(int regenTenths)
        {
            if (regenTenths < 0 || regenTenths > RegenTextCacheTenths)
            {
                return "recovery in " + (regenTenths / 10f).ToString("0.0") + "s";
            }

            string text = s_regenTexts[regenTenths];
            if (text == null)
            {
                text = "recovery in " + (regenTenths / 10f).ToString("0.0") + "s";
                s_regenTexts[regenTenths] = text;
            }

            return text;
        }

        // ============================================================ classic weapon

        /// <summary>Name, ammo and reload from the weapon surface's snapshot. Build is <see cref="ApplyBuild"/>.</summary>
        public RunHudField ApplyWeapon(in ClassicWeaponHudState state)
        {
            RunHudField changed = RunHudField.None;

            // Both strings are cached by their producers (WeaponModifierState.DescribeBuild
            // caches; the display name is a config string), so on a quiet frame the
            // string comparison below short-circuits on reference equality.
            if (state.DisplayName != _weaponSource)
            {
                _weaponSource = state.DisplayName;
                WeaponName = string.IsNullOrEmpty(_weaponSource)
                    ? Placeholder
                    : UiFactory.Clip(_weaponSource.ToUpperInvariant(), WeaponNameMaxCharacters);
                changed |= RunHudField.WeaponName;
            }

            if (state.AmmoInMagazine != _ammo || state.MagazineSize != _magazine || state.PelletCount != PelletCount)
            {
                _ammo = state.AmmoInMagazine;
                _magazine = state.MagazineSize;
                PelletCount = state.PelletCount;
                AmmoText = _magazine > 0 ? _ammo + " / " + _magazine : Placeholder;
                changed |= RunHudField.Ammo;
            }

            RunHudReloadState reload = state.IsReloading
                ? RunHudReloadState.Reloading
                : state.IsEmpty && state.MagazineSize > 0
                    ? RunHudReloadState.MagazineEmpty
                    : RunHudReloadState.Ready;

            float progress = reload == RunHudReloadState.Reloading
                ? Mathf.Clamp01(state.ReloadProgress01)
                : reload == RunHudReloadState.MagazineEmpty ? 0f : 1f;

            if (reload != _reload || !Mathf.Approximately(progress, ReloadProgress01))
            {
                _reload = reload;
                ReloadState = reload;
                ReloadProgress01 = progress;
                changed |= RunHudField.Reload;
            }

            if (state.TemporarilyDisabled != _suspended)
            {
                _suspended = state.TemporarilyDisabled;
                changed |= RunHudField.Suspended;
            }

            return changed;
        }

        // ====================================================================== build

        /// <summary>
        /// The run build line, from <c>RunBuildState.DescribeBuild()</c> - the same
        /// producer the Results screen quotes, so the HUD and Results can never disagree
        /// about what was bought. The producer caches the string; a quiet frame compares
        /// one reference.
        /// </summary>
        public RunHudField ApplyBuild(string description)
        {
            if (description == _buildSource)
            {
                return RunHudField.None;
            }

            _buildSource = description;
            BuildText = string.IsNullOrEmpty(_buildSource) || _buildSource == "Stock"
                ? "no upgrades"
                : UiFactory.Clip(_buildSource, BuildMaxCharacters);
            return RunHudField.Build;
        }

        // ===================================================================== reset

        /// <summary>
        /// Forgets everything, so the next Apply* redraws every field. Called when a run
        /// starts: a HUD that survived into a second run must not keep showing the first
        /// run's last gold, build, round or weapon.
        /// </summary>
        public void Reset()
        {
            _round = int.MinValue;
            _enemiesRemaining = int.MinValue;
            _poolRemaining = int.MinValue;
            _alive = int.MinValue;
            _poolExhausted = false;
            RoundText = Placeholder;
            EnemiesText = Placeholder;
            EnemiesBreakdown = "waiting for the round to start";

            _gold = int.MinValue;
            GoldText = "0";

            _healthCurrent = int.MinValue;
            _healthMax = int.MinValue;
            _healthKnown = false;
            _regenTenths = int.MinValue;
            _dead = false;
            _regenerating = false;
            HealthNormalized = 0f;
            HealthLow = false;
            HealthText = Placeholder;
            HealthStateText = string.Empty;
            HealthStateTone = RunHudTone.Neutral;

            _weaponSource = null;
            _buildSource = null;
            _ammo = int.MinValue;
            _magazine = int.MinValue;
            _reload = (RunHudReloadState)(-1);
            _suspended = false;
            WeaponName = Placeholder;
            BuildText = "no upgrades";
            AmmoText = Placeholder;
            PelletCount = 0;
            ReloadState = RunHudReloadState.Ready;
            ReloadProgress01 = 1f;
        }
    }
}
