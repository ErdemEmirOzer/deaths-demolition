using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Feedback.Vfx
{
    /// <summary>
    /// WP-087 (C-00 05.10) · one casing the VFX threw: the throw it actually got. VfxDressing.EjectCasing picks a per-shot speed
    /// jitter (0.85–1.15 × the weapon's CasingSpeed); the casing sound, placed from the nominal throw, missed the visible casing by
    /// up to 48–64 cm. <see cref="CasingEffect.PredictLanding"/> turns this into the touchdown and rest the visible casing will have.
    /// </summary>
    public readonly struct CasingLaunchInfo
    {
        /// <summary>The controller's ShotSerial of the shot this casing belongs to (the mechanism event's ShotSerial); 0 = unknown.</summary>
        public readonly int ShotSerial;
        public readonly ClassicWeaponId Weapon;

        /// <summary>Where it was born: the rig's Ejection socket.</summary>
        public readonly Vector3 Position;

        /// <summary>The launch velocity it flies with (direction × CasingSpeed × <see cref="SpeedScale"/>), m/s.</summary>
        public readonly Vector3 Velocity;

        /// <summary>The per-shot jitter applied (1 = the profile's nominal CasingSpeed).</summary>
        public readonly float SpeedScale;

        public CasingLaunchInfo(int shotSerial, ClassicWeaponId weapon, Vector3 position, Vector3 velocity, float speedScale)
        {
            ShotSerial = shotSerial;
            Weapon = weapon;
            Position = position;
            Velocity = velocity;
            SpeedScale = speedScale;
        }
    }

    /// <summary>
    /// WP-087 (C-00 05.10) · <see cref="Launched"/> fires once per visible casing, the frame it leaves the port, right after the casing
    /// starts (in VfxDressing.EjectCasing): an A/B weapon's in the shot's frame, before its CasingEjected mechanism event; sg_1's pump
    /// and sg_2's break-open shells from inside that event. No allocation (a struct payload).
    /// </summary>
    public static class CasingEvents
    {
        public static event Action<CasingLaunchInfo> Launched;

        /// <summary>Casings announced since the last reset (tests).</summary>
        public static int LaunchedCount { get; private set; }

        internal static void RaiseLaunched(in CasingLaunchInfo info)
        {
            LaunchedCount++;
            Launched?.Invoke(info);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatics()
        {
            Launched = null;
            LaunchedCount = 0;
        }
    }
}
