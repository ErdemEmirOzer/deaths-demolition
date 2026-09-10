using System;
using DD.Prototype.Core.Config;
using UnityEngine;

namespace DD.Prototype.Weapons
{
    /// <summary>
    /// Where anything that needs the armed player finds it.
    ///
    /// Same idiom as <c>InputSourceRegistry</c> in the core: a lookup for one live
    /// component, not a gameplay singleton. It holds no run state, nothing about the
    /// player's health or position is reachable through it beyond the transform the
    /// weapon is mounted on, and every consumer may ignore it and hold its own
    /// reference instead.
    ///
    /// The upgrade screen, the Special Weapon director and the debug panel all use it so
    /// that none of them has to be wired into the player prefab.
    /// </summary>
    public static class WeaponRegistry
    {
        private static WeaponController s_active;

        /// <summary>Null outside a run. Every caller must handle that.</summary>
        public static WeaponController ActiveController => s_active;

        public static bool HasActiveController => s_active != null;

        /// <summary>Raised when the armed player appears or disappears.</summary>
        public static event Action<WeaponController> ActiveChanged;

        /// <summary>
        /// The classic weapon the next run starts with. The selection screen writes it;
        /// it survives scene loads inside one session and resets on application restart,
        /// which is exactly the behaviour the request asks for.
        /// </summary>
        public static ClassicWeaponId SelectedWeapon { get; set; } = ClassicWeaponId.AssaultRifle;

        /// <summary>Mount point of the live weapon, i.e. roughly the player. Zero when there is none.</summary>
        public static Vector3 PlayerPosition =>
            s_active != null ? s_active.transform.position : Vector3.zero;

        public static void Register(WeaponController controller)
        {
            if (controller == null || s_active == controller)
            {
                return;
            }

            s_active = controller;
            ActiveChanged?.Invoke(controller);
        }

        public static void Unregister(WeaponController controller)
        {
            if (s_active != controller)
            {
                return;
            }

            s_active = null;
            ActiveChanged?.Invoke(null);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_active = null;
            ActiveChanged = null;
            SelectedWeapon = ClassicWeaponId.AssaultRifle;
        }
    }
}
