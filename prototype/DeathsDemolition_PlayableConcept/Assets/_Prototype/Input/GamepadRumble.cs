using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Damage;
using DD.Prototype.Feedback;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DD.Prototype.Inputs
{
    /// <summary>
    /// WP-024: low-intensity gamepad rumble for fire / hit taken / explosion. Listens
    /// to the existing feedback bus (<see cref="AudioEvents.Cue"/>) and the damage bus
    /// (<see cref="DamageEvents.PlayerAttacked"/>) — no new event path. Every pulse is
    /// capped at <see cref="InputPreferences.MaxRumbleIntensity"/> (0.3) and
    /// <see cref="InputPreferences.MaxRumbleMilliseconds"/> (120 ms); the player scales
    /// it further or switches it off in <see cref="InputPreferences"/>. Pause / scene
    /// change / disable stop the motors. Owned by the installer; nothing else creates one.
    /// </summary>
    /// <remarks>
    /// WP-095 (R3, C-00 K3 08.10): top-down is driven by <see cref="FeelEvents"/> through <c>FeelProfileTD</c> rows
    /// (GamepadRumble.TopDown.cs: ceiling 1.0 / 400 ms, the player's scale defaults to 0.7, one motor write per frame). The cue
    /// path below is the first / third person path, unchanged at the player's default (<see cref="LegacyScale"/>).
    /// </remarks>
    public sealed partial class GamepadRumble : IDisposable
    {
        // PROTOTYPE-ONLY pulse table (fractions of the preference ceiling; durations in ms).
        [PrototypeOnly(PendingDecisions.None, "Classic weapon shot pulse when no weapon profile is known: 0.5 x intensity ceiling, 60 ms (WP-042: the equipped weapon's RumbleFraction / RumbleMilliseconds override it).", true)]
        public const float FireFraction = 0.5f;
        public const int FireMilliseconds = 60;

        /// <summary>WP-042: the equipped classic weapon's pulse (ClassicWeaponSettings.RumbleFraction / RumbleMilliseconds), cached at equip — not read on the fire path.</summary>
        public float WeaponFireFraction { get; private set; } = FireFraction;
        public int WeaponFireMilliseconds { get; private set; } = FireMilliseconds;

        /// <summary>WP-056: the active Special Weapon's pulse (SpecialWeaponCommon.RumbleFraction / RumbleMilliseconds), cached when the Special snapshot turns Active.</summary>
        public float SpecialFireFraction { get; private set; } = HeavyFireFraction;
        public int SpecialFireMilliseconds { get; private set; } = HeavyFireMilliseconds;
        private SpecialWeaponId _specialProfileId = (SpecialWeaponId)(-1);
        private ClassicWeaponId _classicProfileId = ClassicWeaponId.AssaultRifle;
        private ulong _feelRevision;

        [PrototypeOnly(PendingDecisions.None, "Heavy (Special) weapon shot pulse: 0.7 x ceiling, 80 ms.", true)]
        public const float HeavyFireFraction = 0.7f;
        public const int HeavyFireMilliseconds = 80;

        [PrototypeOnly(PendingDecisions.None, "Hit taken pulse: 1.0 x ceiling, 120 ms.", true)]
        public const float HitTakenFraction = 1f;
        public const int HitTakenMilliseconds = 120;

        [PrototypeOnly(PendingDecisions.None, "Explosion pulse: 0.8 x ceiling, 100 ms.", true)]
        public const float ExplosionFraction = 0.8f;
        public const int ExplosionMilliseconds = 100;

        private bool _subscribed;
        private bool _disposed;
        private float _stopAt = float.NegativeInfinity;
        private float _activeLow;
        private float _activeHigh;
        private bool _motorsOn;

        /// <summary>Number of pulses requested since creation (F1 readout / tests). Counts even without a gamepad.</summary>
        public int PulseCount { get; private set; }

        /// <summary>Last pulse actually sent to a motor (0 when no gamepad / rumble off).</summary>
        public float LastIntensity { get; private set; }

        public void Subscribe()
        {
            if (_subscribed || _disposed)
            {
                return;
            }

            AudioEvents.Cue += OnCue;
            DamageEvents.PlayerAttacked += OnPlayerAttacked;
            WeaponEvents.WeaponEquipped += OnWeaponEquipped;
            WeaponEvents.SpecialChanged += OnSpecialChanged;
            SubscribeTopDown();
            _subscribed = true;
        }

        /// <summary>WP-056: the Special snapshot left the Active phase → back to the WP-024 default pulse.</summary>
        private void OnSpecialChanged(SpecialWeaponHudState state)
        {
            if (state.Phase != SpecialWeaponPhase.Active)
            {
                _specialProfileId = (SpecialWeaponId)(-1);
                SpecialFireFraction = HeavyFireFraction;
                SpecialFireMilliseconds = HeavyFireMilliseconds;
            }
        }

        /// <summary>
        /// WP-056: the active Special's pulse, resolved from the snapshot's weapon id on the first heavy fire
        /// cue of an activation (one config lookup per activation; the activation itself does not notify —
        /// the pickup path owns that single notify, WP-007).
        /// </summary>
        private void EnsureSpecialProfile()
        {
            SpecialWeaponHudState state = WeaponEvents.Special;
            ulong revision = FeelRuntime.Current.Revision;
            if (state.Phase != SpecialWeaponPhase.Active || (_specialProfileId == state.WeaponId && _feelRevision == revision)) return;
            _specialProfileId = state.WeaponId;
            WeaponFeel row = FeelRuntime.Current.Weapon(WeaponFeelIds.From(state.WeaponId));
            SpecialFireFraction = row.rumbleFraction;
            SpecialFireMilliseconds = row.rumbleDurationMs;
            _feelRevision = revision;
        }

        /// <summary>WP-042: cache the equipped weapon's pulse profile (equip is not a hot path; the fire cue reads two fields).</summary>
        private void OnWeaponEquipped(ClassicWeaponId id)
        {
            _classicProfileId = id;
            RefreshClassicProfile();
        }

        private void RefreshClassicProfile()
        {
            FeelSnapshot feel = FeelRuntime.Current;
            WeaponFeel row = feel.Weapon(WeaponFeelIds.From(_classicProfileId));
            WeaponFireFraction = row.rumbleFraction;
            WeaponFireMilliseconds = row.rumbleDurationMs;
            _feelRevision = feel.Revision;
        }

        public void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            AudioEvents.Cue -= OnCue;
            DamageEvents.PlayerAttacked -= OnPlayerAttacked;
            WeaponEvents.WeaponEquipped -= OnWeaponEquipped;
            WeaponEvents.SpecialChanged -= OnSpecialChanged;
            UnsubscribeTopDown();
            _subscribed = false;
            StopMotors();
        }

        /// <summary>Called once per frame by the installer (unscaled time; pause stops the motors).</summary>
        public void Tick()
        {
            if (_feelRevision != FeelRuntime.Current.Revision) RefreshClassicProfile();
            if (IsTopDown)
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                using var feelCostScope = DD.Prototype.Feedback.FeelCostMeasurement.Enter(DD.Prototype.Feedback.FeelCostPath.Rumble);
                if (feelCostScope.Skip) return;
#endif
                TickTopDown();
                return;
            }

            if (_tdWritten) StopTopDownMotors();
            if (!_motorsOn)
            {
                return;
            }

            if (Time.unscaledTime >= _stopAt || Mathf.Approximately(Time.timeScale, 0f))
            {
                StopMotors();
            }
        }

        /// <summary>Request a pulse. Fraction is of the preference ceiling; clamped to the request limits.</summary>
        public void Pulse(float fraction, int milliseconds)
        {
            if (_disposed)
            {
                return;
            }

            PulseCount++;

            // No gamepad: nothing to drive. Checked FIRST so the hit / explosion path
            // (this is called from the feedback bus) never touches the preferences store
            // — its lazy first load allocates, and the damage path is allocation-free by
            // contract (WP-006 test).
            Gamepad pad = Gamepad.current;
            if (pad == null)
            {
                return;
            }

            InputPreferences prefs = InputPreferencesStore.Current;
            if (!prefs.RumbleEnabled)
            {
                return;
            }

            // WP-095 K3: the preference is now the player's 0..1 scale (default 0.7) over a 1.0 ceiling; this first / third person
            // path keeps its WP-024 numbers — at the default scale exactly the old 0.3 ceiling and 120 ms.
            float intensity = Mathf.Clamp(LegacyScale(prefs.RumbleIntensity) * Mathf.Clamp01(fraction), 0f, InputPreferences.LegacyRumbleCeiling);
            int ms = Mathf.Clamp(milliseconds, 0, InputPreferences.LegacyRumbleMilliseconds);
            if (intensity <= 0f || ms <= 0)
            {
                return;
            }

            // A stronger pulse replaces a weaker one; a weaker one never cuts a stronger one short.
            float low = Mathf.Max(_motorsOn ? _activeLow : 0f, intensity);
            float high = Mathf.Max(_motorsOn ? _activeHigh : 0f, intensity * FeelRuntime.Current.Rumble.highMotorRatio);
            float until = Time.unscaledTime + ms / 1000f;

            pad.SetMotorSpeeds(low, high);
            _activeLow = low;
            _activeHigh = high;
            _stopAt = Mathf.Max(_stopAt, until);
            _motorsOn = true;
            LastIntensity = intensity;
        }

        public void StopMotors()
        {
            if (!_motorsOn)
            {
                return;
            }

            _motorsOn = false;
            _activeLow = 0f;
            _activeHigh = 0f;
            _stopAt = float.NegativeInfinity;

            Gamepad pad = Gamepad.current;
            if (pad != null)
            {
                pad.SetMotorSpeeds(0f, 0f);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Unsubscribe();
            _disposed = true;
        }

        /// <summary>The WP-024 ceiling the player's scale maps to on the first / third person path (default 0.7 → 0.3).</summary>
        public static float LegacyScale(float playerScale) =>
            InputPreferences.LegacyRumbleCeiling * Mathf.Clamp01(playerScale / InputPreferences.DefaultRumbleScale);

        private void OnCue(CombatAudioCue cue)
        {
            if (IsTopDown) return;   // WP-095: top-down rumbles from FeelEvents (GamepadRumble.TopDown.cs)
            switch (cue.Event)
            {
                case CombatAudioEvent.ClassicWeaponFire:
                    Pulse(WeaponFireFraction, WeaponFireMilliseconds);
                    break;
                case CombatAudioEvent.HeavyWeaponFire:
                    EnsureSpecialProfile();
                    Pulse(SpecialFireFraction, SpecialFireMilliseconds);
                    break;
                case CombatAudioEvent.Explosion:
                    Pulse(ExplosionFraction, ExplosionMilliseconds);
                    break;
            }
        }

        private void OnPlayerAttacked(EnemyAttackLandedInfo info)
        {
            if (IsTopDown) return;
            Pulse(HitTakenFraction, HitTakenMilliseconds);
        }
    }
}
