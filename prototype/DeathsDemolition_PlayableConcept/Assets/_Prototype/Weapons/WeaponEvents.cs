using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.Weapons;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>Where the one Special Weapon of the run currently is.</summary>
    public enum SpecialWeaponPhase
    {
        /// <summary>Nothing spawned. Nothing to draw.</summary>
        None = 0,

        /// <summary>Lying on the map, its own ground timer running (REQ-055).</summary>
        OnGround = 1,

        /// <summary>Picked up and firing, active timer running (REQ-055, REQ-058).</summary>
        Active = 2
    }

    /// <summary>
    /// WP-007: why a Special Weapon process stopped before its own timers did. Carried by
    /// <see cref="WeaponEvents.SpecialWeaponCancelled"/> so a consumer can tell "cancelled"
    /// from "expired" (they used to arrive as the same Ended event).
    /// </summary>
    public enum SpecialWeaponCancelReason
    {
        /// <summary>The run ended (player death, run end) while a process was scheduled, on the ground or in hand.</summary>
        RunEnded = 1,

        /// <summary>A new run started with a process still alive (same-scene restart).</summary>
        RunStarted = 2,

        /// <summary>
        /// The round ended while the drop was still scheduled (it simply never happens) or while the Special was in
        /// hand (PhaseAtCancel Active: it ends with the round, DEC-099 D6).
        /// </summary>
        RoundEnded = 3,

        /// <summary>No valid, reachable drop position existed for the whole round: the scheduled drop was abandoned.</summary>
        NoValidPosition = 4,

        /// <summary>The owning object was disabled / torn down (scene unload, director or controller shutdown).</summary>
        Teardown = 5,

        /// <summary>Ended by the debug panel or a diagnostic.</summary>
        Debug = 6
    }

    /// <summary>Payload of <see cref="WeaponEvents.SpecialWeaponCancelled"/>.</summary>
    public readonly struct SpecialWeaponCancelInfo
    {
        public readonly SpecialWeaponCancelReason Reason;

        /// <summary>Where the process was when it was cancelled: OnGround, Active, or None for a merely scheduled drop.</summary>
        public readonly SpecialWeaponPhase PhaseAtCancel;

        public SpecialWeaponCancelInfo(SpecialWeaponCancelReason reason, SpecialWeaponPhase phaseAtCancel)
        {
            Reason = reason;
            PhaseAtCancel = phaseAtCancel;
        }
    }

    /// <summary>What a WP-042 weapon-mechanism event carries: the weapon and the socket it happened at.</summary>
    public enum WeaponMechanismKind
    {
        /// <summary>A casing left the ejection port (clip event <c>eject</c>; A/B every shot, C at the pump).</summary>
        CasingEjected = 0,

        /// <summary>The magazine left the weapon (clip event <c>mag_out</c>; A/B only — C loads shells).</summary>
        MagazineDropped = 1,

        /// <summary>A shell was seated (clip event <c>mag_in</c>; C's three shells, A/B once).</summary>
        ShellSeated = 2,

        /// <summary>The bolt / pump closed (clip event <c>chamber</c>).</summary>
        Chambered = 3
    }

    /// <summary>
    /// WP-042 · payload of <see cref="WeaponEvents.Mechanism"/>: a slot for the S-07 VFX
    /// (AP-010B <c>mag_drop</c> / <c>spark</c>) and S-08 SFX layers. Position / direction are
    /// the socket's world frame at the event (Ejection +X for a casing, the mag marker down
    /// for a magazine); <see cref="FromClipEvent"/> tells a clip-driven slot from the timer
    /// fallback the controller uses until the AP-009C clips carry the event.
    /// </summary>
    public readonly struct WeaponMechanismEvent
    {
        public readonly WeaponMechanismKind Kind;
        public readonly ClassicWeaponId Weapon;
        public readonly Vector3 Position;
        public readonly Vector3 Direction;
        public readonly bool FromClipEvent;

        public WeaponMechanismEvent(WeaponMechanismKind kind, ClassicWeaponId weapon, Vector3 position, Vector3 direction, bool fromClipEvent)
        {
            Kind = kind;
            Weapon = weapon;
            Position = position;
            Direction = direction;
            FromClipEvent = fromClipEvent;
        }
    }

    /// <summary>Everything the HUD needs about the classic weapon. Poll it, or listen for changes.</summary>
    public struct ClassicWeaponHudState
    {
        public ClassicWeaponId Id;
        public string DisplayName;

        public int AmmoInMagazine;
        public int MagazineSize;

        public bool IsReloading;

        /// <summary>0 at the start of the reload, 1 when the magazine is in.</summary>
        public float ReloadProgress01;

        public float ReloadRemaining;
        public float ReloadDuration;

        /// <summary>
        /// True while a Special Weapon is active. The classic weapon is NOT gone
        /// (REQ-057) - draw it dimmed, not empty.
        /// </summary>
        public bool TemporarilyDisabled;

        /// <summary>Short build identity, e.g. "P2 / FR1". Empty-safe.</summary>
        public string BuildDescription;

        /// <summary>
        /// WP-068 U12a (honest reticle): the cone half-angle in degrees (horizontal / yaw) the NEXT shot
        /// rolls against — base SpreadDegrees + fire bloom, the very value the shot builder passes to
        /// <see cref="SpreadUtility"/>. WP-069 B2: movement, focus / ADS and the settle curve are already in it. Data only.
        /// </summary>
        public float CurrentSpreadDegrees;

        /// <summary>WP-068 U12a: pellets per trigger pull (1 for single-shot weapons).</summary>
        public int PelletCount;

        /// <summary>WP-069 (S-09 HUD "30 / 120"): rounds carried beyond the magazine; meaningless when <see cref="ReserveInfinite"/>.</summary>
        public int ReserveAmmo;

        /// <summary>WP-069: the weapon has no reserve limit (the M3 behaviour; HUD shows ∞).</summary>
        public bool ReserveInfinite;

        /// <summary>WP-069 B2: Auto / Burst / Single (HUD fire-mode pip).</summary>
        public WeaponFireMode FireMode;

        /// <summary>WP-069 B2: 0 = hip, 1 = fully focused (top-down) / aimed down the sights (look modes).</summary>
        public float AdsWeight01;

        /// <summary>WP-069 B2: magazine and reserve both empty — progress 0..1 to the floor's free magazine; 0 otherwise.</summary>
        public float FloorRefill01;

        /// <summary>
        /// WP-068 U12a: horizontal half-angle of the WP-042 pellet ring (PelletRingRadiusShare ×
        /// <see cref="CurrentSpreadDegrees"/>) — the reticle's second ring. 0 when the weapon fires a
        /// single pellet or the independent scatter.
        /// </summary>
        public float PelletRingDegrees;

        /// <summary>Convenience for the "reload!" prompt.</summary>
        public bool IsEmpty => AmmoInMagazine <= 0;

        /// <summary>WP-068 U12a: lateral radius of the spread cone at <paramref name="meters"/> (the reticle's projection input). No allocation.</summary>
        public float SpreadRadiusAt(float meters)
        {
            return Mathf.Max(0f, meters) * Mathf.Tan(Mathf.Clamp(CurrentSpreadDegrees, 0f, 89f) * Mathf.Deg2Rad);
        }
    }

    /// <summary>Everything the HUD needs about the Special Weapon.</summary>
    public struct SpecialWeaponHudState
    {
        public SpecialWeaponPhase Phase;

        /// <summary>Which of the three weapons this drop is. Valid in both phases.</summary>
        public SpecialWeaponId WeaponId;

        public string DisplayName;

        /// <summary>Valid while <see cref="Phase"/> is OnGround. The world marker points here.</summary>
        public Vector3 WorldPosition;

        public float GroundRemaining;
        public float GroundDuration;

        /// <summary>
        /// G-21. The ground blink phase, integrated by the pickup with
        /// <see cref="DD.Prototype.Feedback.GroundBlinkClock"/>.
        ///
        /// The HUD marker does NOT compute its own: the world beacon and the HUD marker
        /// have to flash on the same beat, and the only way to guarantee that rather than
        /// hope for it is to publish one phase and let both surfaces read it. A HUD that
        /// ran its own clock would be right about the rate and wrong about the beat, which
        /// is the one thing a player notices.
        ///
        /// The GROUND timer has no numeric readout any more. This is what replaced it.
        /// </summary>
        public float GroundBlinkPhase;

        public float ActiveRemaining;
        public float ActiveDuration;

        /// <summary>True during the last seconds of the ACTIVE timer (REQ-058).</summary>
        public bool FinalSecondsWarning;

        // ---- WP-069 B4 (DEC-097, DIRECTION §7c): the Special is a magazine weapon with a time cap; the HUD shows
        // magazine / reserve and a secondary cap strip (ActiveRemaining / ActiveDuration are that cap now: 40 s / 45 s).

        public int AmmoInMagazine;
        public int MagazineSize;
        public int ReserveAmmo;
        public bool IsReloading;
        public float ReloadProgress01;

        /// <summary>Raiju's 0.25 s charge, 0..1 while the trigger is held (0 otherwise).</summary>
        public float Charge01;

        /// <summary>What ends the activation: ammo, time, or whichever comes first.</summary>
        public SpecialLimit Limit;

        public float GroundProgress01 => GroundDuration > 0f ? Mathf.Clamp01(GroundRemaining / GroundDuration) : 0f;

        public float ActiveProgress01 => ActiveDuration > 0f ? Mathf.Clamp01(ActiveRemaining / ActiveDuration) : 0f;
    }

    /// <summary>
    /// The read-only weapon surface for the HUD (G-08) and for anything else that wants
    /// to watch the weapon without owning it.
    ///
    /// Two access styles on purpose:
    ///
    ///   * <see cref="Classic"/> and <see cref="Special"/> are snapshots, refreshed every
    ///     frame by the weapon. A countdown label should read these in its own Update;
    ///     firing an event sixty times a second so a HUD can print a number is waste.
    ///   * The events fire on state CHANGES only - equip, reload start/end, dry fire,
    ///     spawn, pickup, expiry, final-seconds. Those are the moments a HUD wants to
    ///     animate, flash or play a transition.
    ///
    /// Everything named Publish* is written by the weapon systems. Consumers must treat
    /// them as private.
    /// </summary>
    public static class WeaponEvents
    {
        // ------------------------------------------------------------------ snapshots

        /// <summary>Current classic weapon state. Refreshed every frame while a run is live.</summary>
        public static ClassicWeaponHudState Classic { get; private set; }

        /// <summary>Current Special Weapon state. Refreshed every frame while a run is live.</summary>
        public static SpecialWeaponHudState Special { get; private set; }

        // ------------------------------------------------------------------ classic events

        /// <summary>A run started with this weapon, or the debug panel swapped it.</summary>
        public static event Action<ClassicWeaponId> WeaponEquipped;

        /// <summary>Ammo count changed, for the odometer animation.</summary>
        public static event Action<int, int> AmmoChanged;

        /// <summary>Reload began. Argument is the full duration in seconds, upgrades included.</summary>
        public static event Action<float> ReloadStarted;

        /// <summary>Reload finished and the magazine is full.</summary>
        public static event Action ReloadCompleted;

        /// <summary>Reload was cut short - the Special Weapon was picked up mid-reload.</summary>
        public static event Action ReloadInterrupted;

        /// <summary>Trigger pulled with an empty magazine.</summary>
        public static event Action DryFire;

        /// <summary>Any discrete change to the classic weapon snapshot.</summary>
        public static event Action<ClassicWeaponHudState> ClassicChanged;

        /// <summary>WP-069 (S-08 looped fire, S-07 sustained-fire model): the trigger started producing shots.</summary>
        public static event Action<ClassicWeaponId> FireStarted;

        /// <summary>WP-069: the trigger stopped producing shots (released, empty, reload, Special, death).</summary>
        public static event Action<ClassicWeaponId> FireStopped;

        public static void PublishFireStarted(ClassicWeaponId id) => FireStarted?.Invoke(id);

        public static void PublishFireStopped(ClassicWeaponId id) => FireStopped?.Invoke(id);

        /// <summary>WP-042: a casing / magazine / shell / chamber slot fired (see <see cref="WeaponMechanismEvent"/>). Read-only for effects and audio.</summary>
        public static event Action<WeaponMechanismEvent> Mechanism;

        /// <summary>Mechanism events published since the last reset (tests / probes).</summary>
        public static int MechanismCount { get; private set; }

        // ------------------------------------------------------------------ special events

        /// <summary>
        /// REQ-053. A Special Weapon landed on the map. Arguments: world position and
        /// the ground lifetime it starts with. The HUD marker should appear here.
        /// </summary>
        public static event Action<Vector3, float> SpecialWeaponSpawned;

        /// <summary>Nobody collected it in time; the marker must disappear.</summary>
        public static event Action SpecialWeaponGroundExpired;

        /// <summary>Collected. Argument is the active duration in seconds (REQ-055).</summary>
        public static event Action<float> SpecialWeaponPickedUp;

        /// <summary>
        /// REQ-058. Fired once when the active timer enters its warning window.
        /// Argument is the remaining time at that moment.
        /// </summary>
        public static event Action<float> SpecialWeaponFinalSeconds;

        /// <summary>Active duration ended; the classic weapon is back (REQ-057).</summary>
        public static event Action SpecialWeaponEnded;

        /// <summary>Any phase transition of the Special Weapon.</summary>
        public static event Action<SpecialWeaponHudState> SpecialChanged;

        // ------------------------------------------------------------------ WP-007 lifecycle transitions
        // Added for observability only (OS-2); the six events above keep their payloads.

        /// <summary>A drop was scheduled for this round. Argument: the round-progress mark it waits for (0..1).</summary>
        public static event Action<float> SpecialWeaponScheduled;

        /// <summary>
        /// A scheduled, grounded or active Special Weapon was stopped by something other than
        /// its own timers (run end, death, new run, round end before the drop, no valid
        /// position, teardown, debug). For an active weapon <see cref="SpecialWeaponEnded"/>
        /// still follows, so consumers of "the classic weapon is back" need no change.
        /// </summary>
        public static event Action<SpecialWeaponCancelInfo> SpecialWeaponCancelled;

        /// <summary>
        /// The director is fully back to Idle: no pickup, no runtime, no timer, no beacon, no
        /// loop, snapshot None. Raised once per process (after expiry, after the active
        /// duration, or after a cancel). A second drop may only be considered after this.
        /// </summary>
        public static event Action SpecialWeaponCleared;

        // ------------------------------------------------------------------ producers

        public static void PublishClassic(in ClassicWeaponHudState state, bool notify)
        {
            int previousAmmo = Classic.AmmoInMagazine;
            Classic = state;

            if (previousAmmo != state.AmmoInMagazine)
            {
                AmmoChanged?.Invoke(state.AmmoInMagazine, state.MagazineSize);
            }

            if (notify)
            {
                ClassicChanged?.Invoke(state);
            }
        }

        public static void PublishSpecial(in SpecialWeaponHudState state, bool notify)
        {
            Special = state;

            if (notify)
            {
                SpecialChanged?.Invoke(state);
            }
        }

        public static void PublishWeaponEquipped(ClassicWeaponId id) => WeaponEquipped?.Invoke(id);

        /// <summary>WP-069 (S-06 B4b): the reload <see cref="ReloadStarted"/> just announced is the empty one (dry chamber) — read inside the handler.</summary>
        public static bool LastReloadEmpty { get; private set; }

        public static void PublishReloadStarted(float duration) => PublishReloadStarted(duration, false);

        public static void PublishReloadStarted(float duration, bool empty)
        {
            LastReloadEmpty = empty;
            ReloadStarted?.Invoke(duration);
        }

        public static void PublishReloadCompleted() => ReloadCompleted?.Invoke();

        public static void PublishReloadInterrupted() => ReloadInterrupted?.Invoke();

        public static void PublishDryFire() => DryFire?.Invoke();

        public static void PublishMechanism(in WeaponMechanismEvent e)
        {
            MechanismCount++;
            Mechanism?.Invoke(e);
        }

        public static void PublishSpecialSpawned(Vector3 position, float groundLifetime)
            => SpecialWeaponSpawned?.Invoke(position, groundLifetime);

        public static void PublishSpecialGroundExpired() => SpecialWeaponGroundExpired?.Invoke();

        public static void PublishSpecialPickedUp(float activeDuration)
            => SpecialWeaponPickedUp?.Invoke(activeDuration);

        public static void PublishSpecialFinalSeconds(float remaining)
            => SpecialWeaponFinalSeconds?.Invoke(remaining);

        public static void PublishSpecialEnded() => SpecialWeaponEnded?.Invoke();

        public static void PublishSpecialScheduled(float progressMark) => SpecialWeaponScheduled?.Invoke(progressMark);

        public static void PublishSpecialCancelled(SpecialWeaponCancelReason reason, SpecialWeaponPhase phaseAtCancel)
            => SpecialWeaponCancelled?.Invoke(new SpecialWeaponCancelInfo(reason, phaseAtCancel));

        public static void PublishSpecialCleared() => SpecialWeaponCleared?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Classic = default;
            Special = default;

            WeaponEquipped = null;
            AmmoChanged = null;
            ReloadStarted = null;
            ReloadCompleted = null;
            ReloadInterrupted = null;
            DryFire = null;
            ClassicChanged = null;
            FireStarted = null;
            FireStopped = null;

            SpecialWeaponSpawned = null;
            SpecialWeaponGroundExpired = null;
            SpecialWeaponPickedUp = null;
            SpecialWeaponFinalSeconds = null;
            SpecialWeaponEnded = null;
            SpecialChanged = null;

            SpecialWeaponScheduled = null;
            SpecialWeaponCancelled = null;
            SpecialWeaponCleared = null;
        }
    }
}
