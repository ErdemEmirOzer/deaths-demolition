using System;
using DD.Prototype.Core.Config;
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

        /// <summary>Convenience for the "reload!" prompt.</summary>
        public bool IsEmpty => AmmoInMagazine <= 0;
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

        public static void PublishReloadStarted(float duration) => ReloadStarted?.Invoke(duration);

        public static void PublishReloadCompleted() => ReloadCompleted?.Invoke();

        public static void PublishReloadInterrupted() => ReloadInterrupted?.Invoke();

        public static void PublishDryFire() => DryFire?.Invoke();

        public static void PublishSpecialSpawned(Vector3 position, float groundLifetime)
            => SpecialWeaponSpawned?.Invoke(position, groundLifetime);

        public static void PublishSpecialGroundExpired() => SpecialWeaponGroundExpired?.Invoke();

        public static void PublishSpecialPickedUp(float activeDuration)
            => SpecialWeaponPickedUp?.Invoke(activeDuration);

        public static void PublishSpecialFinalSeconds(float remaining)
            => SpecialWeaponFinalSeconds?.Invoke(remaining);

        public static void PublishSpecialEnded() => SpecialWeaponEnded?.Invoke();

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

            SpecialWeaponSpawned = null;
            SpecialWeaponGroundExpired = null;
            SpecialWeaponPickedUp = null;
            SpecialWeaponFinalSeconds = null;
            SpecialWeaponEnded = null;
            SpecialChanged = null;
        }
    }
}
