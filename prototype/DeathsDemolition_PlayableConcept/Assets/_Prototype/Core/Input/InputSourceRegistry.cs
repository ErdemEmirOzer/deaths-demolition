using System;
using UnityEngine;

namespace DD.Prototype.Core.InputAbstraction
{
    /// <summary>
    /// Where consumers find the live <see cref="IInputSource"/>.
    ///
    /// This is a lookup for a device adapter, not a gameplay singleton: no game state
    /// lives here, and any system may ignore it and take an injected IInputSource
    /// instead. Nothing about the player character is reachable through it.
    /// </summary>
    public static class InputSourceRegistry
    {
        private static IInputSource s_current = NullInputSource.Instance;

        /// <summary>Never null. Falls back to <see cref="NullInputSource"/>.</summary>
        public static IInputSource Current => s_current ?? NullInputSource.Instance;

        /// <summary>Raised when the concrete source is installed or removed.</summary>
        public static event Action<IInputSource> CurrentChanged;

        /// <summary>True once a real device-backed source has been installed.</summary>
        public static bool HasRealSource => s_current != null && !(s_current is NullInputSource);

        public static void SetCurrent(IInputSource source)
        {
            IInputSource next = source ?? NullInputSource.Instance;
            if (ReferenceEquals(next, s_current))
            {
                return;
            }

            s_current = next;
            CurrentChanged?.Invoke(next);
        }

        public static void Clear()
        {
            SetCurrent(NullInputSource.Instance);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = NullInputSource.Instance;
            CurrentChanged = null;
        }
    }
}
