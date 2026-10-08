using System;
using DD.Prototype.CameraSystem;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using DD.Prototype.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DD.Prototype.Inputs
{
    /// <summary>
    /// WP-095 (R3) · the top-down rumble mix (his sözleşmesi §3 / §4.0 / §4.7; C-00 K3 08.10). Pure state, no Unity calls:
    /// one event slot (the stronger request wins, a weaker one never cuts it short), one sustained-fire level (held while an
    /// automatic's trigger is down, faded out after release), one scheduled second pulse (the heartbeat's "dub"). Sampled
    /// once per frame; an event over sustained fire rides <see cref="FeelProfileTD.rumbleEventOverSustained"/> above it.
    /// </summary>
    internal sealed class TopDownRumbleMixer
    {
        private float _eventLow, _eventHigh, _eventUntil = float.NegativeInfinity;
        private float _sustainLow, _sustainHigh, _sustainFadeFrom = float.PositiveInfinity, _sustainFadeSeconds;
        private float _pendingAt = float.PositiveInfinity, _pendingLow, _pendingHigh, _pendingSeconds;

        public bool EventActive(float now) => now < _eventUntil;

        /// <summary>A one-shot pulse; the stronger of the running and the new one plays, a weaker request is dropped.</summary>
        public void Event(float low, float high, float seconds, float now)
        {
            if (seconds <= 0f || (low <= 0f && high <= 0f)) return;
            bool running = now < _eventUntil;
            if (running && Mathf.Max(low, high) < Mathf.Max(_eventLow, _eventHigh)) return;
            _eventLow = low;
            _eventHigh = high;
            _eventUntil = running ? Mathf.Max(_eventUntil, now + seconds) : now + seconds;
        }

        /// <summary>The heartbeat's second beat (or any follow-up) <paramref name="delay"/> seconds from now; one slot.</summary>
        public void Schedule(float delay, float low, float high, float seconds, float now)
        {
            _pendingAt = now + delay;
            _pendingLow = low;
            _pendingHigh = high;
            _pendingSeconds = seconds;
        }

        /// <summary>Sustained fire on: the level holds until <see cref="SustainOff"/>.</summary>
        public void SustainOn(float low, float high)
        {
            _sustainLow = low;
            _sustainHigh = high;
            _sustainFadeFrom = float.PositiveInfinity;
        }

        /// <summary>Trigger released: the level fades to 0 over <paramref name="fadeSeconds"/>.</summary>
        public void SustainOff(float fadeSeconds, float now)
        {
            if (_sustainLow <= 0f && _sustainHigh <= 0f) return;
            if (!float.IsPositiveInfinity(_sustainFadeFrom)) return;
            _sustainFadeFrom = now;
            _sustainFadeSeconds = Mathf.Max(0.0001f, fadeSeconds);
        }

        public void Clear()
        {
            _eventLow = _eventHigh = 0f;
            _eventUntil = float.NegativeInfinity;
            _sustainLow = _sustainHigh = 0f;
            _sustainFadeFrom = float.PositiveInfinity;
            _pendingAt = float.PositiveInfinity;
        }

        /// <summary>The motor speeds now, before the player's scale; both 0 when nothing plays.</summary>
        public void Sample(float now, float eventOverSustained, float ceiling, out float low, out float high)
        {
            if (now >= _pendingAt)
            {
                _pendingAt = float.PositiveInfinity;
                Event(_pendingLow, _pendingHigh, _pendingSeconds, now);
            }

            float fade = 1f;
            if (!float.IsPositiveInfinity(_sustainFadeFrom))
            {
                fade = 1f - Mathf.Clamp01((now - _sustainFadeFrom) / _sustainFadeSeconds);
                if (fade <= 0f) { _sustainLow = _sustainHigh = 0f; _sustainFadeFrom = float.PositiveInfinity; }
            }

            float sLow = _sustainLow * fade, sHigh = _sustainHigh * fade;
            low = sLow;
            high = sHigh;
            if (now < _eventUntil)
            {
                bool over = sLow > 0f || sHigh > 0f;
                low = Mathf.Max(_eventLow, over ? sLow + eventOverSustained : 0f, sLow);
                high = Mathf.Max(_eventHigh, over ? sHigh + eventOverSustained : 0f, sHigh);
            }

            low = Mathf.Clamp(low, 0f, ceiling);
            high = Mathf.Clamp(high, 0f, ceiling);
        }
    }

    public sealed partial class GamepadRumble
    {
        private readonly TopDownRumbleMixer _td = new TopDownRumbleMixer();
        private bool _tdWritten;
        private float _tdLow, _tdHigh;
        private bool _firingHeld;
        private float _lastGoldPulse = float.NegativeInfinity;

        private Action<FeelShot> _onShot;
        private Action<FeelHit> _onHit;
        private Action<FeelKill> _onKill;
        private Action<FeelMultiKill> _onMulti;
        private Action<FeelExplosion> _onExplosion;
        private Action<FeelPlayerDamage> _onHurt;
        private Action<FeelReload> _onReloadStart, _onReloadEnd;
        private Action<FeelDryFire> _onDry;
        private Action<FeelGold> _onGold;
        private Action<FeelRound> _onRound;
        private Action<FeelLastZombie> _onLast;
        private Action<FeelHeartbeat> _onBeat;
        private Action<ClassicWeaponId> _onFireStart, _onFireStop;

        private static bool IsTopDown => CameraModeService.Current == CameraMode.TopDown;

        private static float Now => Time.unscaledTime;

        private void SubscribeTopDown()
        {
            _onShot ??= OnShotTD; _onHit ??= OnHitTD; _onKill ??= OnKillTD; _onMulti ??= OnMultiTD; _onExplosion ??= OnExplosionTD;
            _onHurt ??= OnHurtTD; _onReloadStart ??= OnReloadStartTD; _onReloadEnd ??= OnReloadEndTD; _onDry ??= OnDryTD;
            _onGold ??= OnGoldTD; _onRound ??= OnRoundTD; _onLast ??= OnLastTD; _onBeat ??= OnBeatTD;
            _onFireStart ??= OnFireStartTD; _onFireStop ??= OnFireStopTD;
            FeelEvents.ShotFired += _onShot; FeelEvents.EnemyHit += _onHit; FeelEvents.EnemyKilled += _onKill;
            FeelEvents.MultiKill += _onMulti; FeelEvents.Explosion += _onExplosion; FeelEvents.PlayerDamaged += _onHurt;
            FeelEvents.ReloadStarted += _onReloadStart; FeelEvents.ReloadEnded += _onReloadEnd; FeelEvents.DryFire += _onDry;
            FeelEvents.GoldCollected += _onGold; FeelEvents.RoundStarted += _onRound; FeelEvents.LastZombie += _onLast;
            FeelEvents.Heartbeat += _onBeat;
            WeaponEvents.FireStarted += _onFireStart; WeaponEvents.FireStopped += _onFireStop;
        }

        private void UnsubscribeTopDown()
        {
            FeelEvents.ShotFired -= _onShot; FeelEvents.EnemyHit -= _onHit; FeelEvents.EnemyKilled -= _onKill;
            FeelEvents.MultiKill -= _onMulti; FeelEvents.Explosion -= _onExplosion; FeelEvents.PlayerDamaged -= _onHurt;
            FeelEvents.ReloadStarted -= _onReloadStart; FeelEvents.ReloadEnded -= _onReloadEnd; FeelEvents.DryFire -= _onDry;
            FeelEvents.GoldCollected -= _onGold; FeelEvents.RoundStarted -= _onRound; FeelEvents.LastZombie -= _onLast;
            FeelEvents.Heartbeat -= _onBeat;
            WeaponEvents.FireStarted -= _onFireStart; WeaponEvents.FireStopped -= _onFireStop;
            _td.Clear();
            StopTopDownMotors();
        }

        /// <summary>Once per frame (installer Update): one motor write when the mix changed; pause stops the motors.</summary>
        private void TickTopDown()
        {
            if (_motorsOn) StopMotors();   // a first / third person pulse left running across the mode change
            float low = 0f, high = 0f;
            Gamepad pad = Gamepad.current;
            if (pad != null && !RunFlowClock.IsFrozen)
            {
                FeelProfileTD profile = FeelProfileTD.Active;
                InputPreferences prefs = InputPreferencesStore.Current;
                if (profile.rumble && prefs.RumbleEnabled)
                {
                    _td.Sample(Now, profile.rumbleEventOverSustained, profile.rumbleCeiling, out low, out high);
                    float scale = Mathf.Clamp01(prefs.RumbleIntensity);
                    low *= scale;
                    high *= scale;
                }
            }
            else if (RunFlowClock.IsFrozen)
            {
                _td.Clear();
            }

            if (pad == null) return;
            if (Mathf.Abs(low - _tdLow) < 0.005f && Mathf.Abs(high - _tdHigh) < 0.005f && _tdWritten == (low > 0f || high > 0f)) return;
            pad.SetMotorSpeeds(low, high);
            _tdLow = low;
            _tdHigh = high;
            _tdWritten = low > 0f || high > 0f;
            if (_tdWritten) LastIntensity = Mathf.Max(low, high);
        }

        private void StopTopDownMotors()
        {
            if (!_tdWritten) return;
            _tdWritten = false;
            _tdLow = _tdHigh = 0f;
            Gamepad pad = Gamepad.current;
            if (pad != null) pad.SetMotorSpeeds(0f, 0f);
        }

        // ------------------------------------------------------------------ FeelEvents → mix (top-down only)

        private void Row(FeelEventId id, float scale = 1f)
        {
            if (!IsTopDown) return;
            EventFeelTD row = FeelProfileTD.Active.Event(id);
            Pulse(row.rumbleLow * scale, row.rumbleHigh * scale, row.rumbleSeconds);
        }

        private void Pulse(float low, float high, float seconds)
        {
            if (seconds <= 0f || (low <= 0f && high <= 0f)) return;
            PulseCount++;
            _td.Event(low, high, Mathf.Min(seconds, FeelProfileTD.Active.rumbleMaxSeconds), Now);
        }

        private void OnShotTD(FeelShot shot)
        {
            if (!IsTopDown) return;
            FeelProfileTD profile = FeelProfileTD.Active;
            WeaponFeelTD w = shot.Special ? profile.Weapon(shot.SpecialId) : profile.Weapon(shot.Classic);
            if (w.rumbleSustained && !shot.Special)
            {
                PulseCount++;
                _td.SustainOn(w.rumbleLow, w.rumbleHigh);
                if (!_firingHeld) _td.SustainOff(w.rumbleSeconds, Now);   // a tap of an automatic: one short buzz
                return;
            }

            Pulse(w.rumbleLow, w.rumbleHigh, w.rumbleSeconds);
        }

        private void OnFireStartTD(ClassicWeaponId id) => _firingHeld = true;

        private void OnFireStopTD(ClassicWeaponId id)
        {
            _firingHeld = false;
            if (!IsTopDown) return;
            _td.SustainOff(FeelProfileTD.Active.Weapon(id).rumbleSeconds, Now);   // §4.1: fades 0.06 s after the trigger lets go
        }

        private void OnHitTD(FeelHit hit)
        {
            if (hit.ArmorBroke) Row(FeelEventId.ArmorBreak);
            else if (hit.Critical && !hit.Killed) Row(FeelEventId.Crit);
        }

        private void OnKillTD(FeelKill kill)
        {
            if (kill.Heavy) Row(kill.Tier == EnemyTier.Ultra ? FeelEventId.HeavyDeath_Ultra : FeelEventId.HeavyDeath_Elite);
            else Row(FeelEventId.Kill);
        }

        private void OnMultiTD(FeelMultiKill m) =>
            Row(m.Tier >= 3 ? FeelEventId.MultiKill_T3 : m.Tier == 2 ? FeelEventId.MultiKill_T2 : FeelEventId.MultiKill_T1);

        private void OnExplosionTD(FeelExplosion e)
        {
            if (!IsTopDown) return;
            PlayerRig player = PlayerRegistry.Primary;
            float a = player != null ? CameraModesFeel.ExplosionFalloff(Vector3.Distance(player.transform.position, e.Origin)) : 1f;
            EventFeelTD row = FeelProfileTD.Active.Event(FeelEventId.Explosion);
            // §4.0: × A(d), at least .20 when the blast is on screen
            float floor = OnScreen(e.Origin) ? 0.20f : 0f;
            float low = Mathf.Max(row.rumbleLow * a, floor), high = Mathf.Max(row.rumbleHigh * a, floor * 0.5f);
            Pulse(low, high, row.rumbleSeconds);
        }

        private static bool OnScreen(Vector3 world)
        {
            Camera cam = Camera.main;
            if (cam == null) return false;
            Vector3 v = cam.WorldToViewportPoint(world);
            return v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }

        private void OnHurtTD(FeelPlayerDamage d)
        {
            if (!IsTopDown) return;
            FeelProfileTD profile = FeelProfileTD.Active;
            if (d.Heavy) { Row(FeelEventId.PlayerDamaged_Heavy); return; }
            // §4.0: Light → Heavy linear in the damage fraction (light below, heavy at the heavy threshold)
            EventFeelTD light = profile.Event(FeelEventId.PlayerDamaged_Light), heavy = profile.Event(FeelEventId.PlayerDamaged_Heavy);
            float t = Mathf.Clamp01(d.Fraction / Mathf.Max(0.01f, FeelEvents.Thresholds.HeavyPlayerHitFraction));
            Pulse(Mathf.Lerp(light.rumbleLow, heavy.rumbleLow, t), Mathf.Lerp(light.rumbleHigh, heavy.rumbleHigh, t),
                Mathf.Lerp(light.rumbleSeconds, heavy.rumbleSeconds, t));
        }

        private void OnReloadStartTD(FeelReload r) => Row(FeelEventId.ReloadStart);

        private void OnReloadEndTD(FeelReload r)
        {
            if (r.Completed) Row(FeelEventId.ReloadEnd);
        }

        private void OnDryTD(FeelDryFire d) => Row(FeelEventId.DryFire);

        private void OnGoldTD(FeelGold g)
        {
            if (!IsTopDown || Now - _lastGoldPulse < 0.1f) return;   // §4.0: ≤ 1 per 0.1 s
            _lastGoldPulse = Now;
            Row(FeelEventId.GoldPickup);
        }

        private void OnRoundTD(FeelRound r) => Row(FeelEventId.RoundStart);

        private void OnLastTD(FeelLastZombie z) => Row(FeelEventId.LastZombie);

        /// <summary>§4.7: .18 low for 0.05 s, then .12 low 0.18 s later; suppressed while firing; its own profile switch.</summary>
        private void OnBeatTD(FeelHeartbeat beat)
        {
            if (!IsTopDown || _firingHeld) return;
            FeelProfileTD profile = FeelProfileTD.Active;
            if (!profile.lowHealthHeartbeatRumble) return;
            EventFeelTD row = profile.Event(FeelEventId.Heartbeat);
            Pulse(row.rumbleLow, row.rumbleHigh, row.rumbleSeconds);
            _td.Schedule(0.18f, row.rumbleLow * (0.12f / 0.18f), row.rumbleHigh, row.rumbleSeconds, Now);
        }
    }
}
