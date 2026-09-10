using System;
using UnityEngine;

namespace DD.Prototype.Core.InputAbstraction
{
    /// <summary>
    /// Does nothing, reports nothing. Installed by default so no consumer ever has to
    /// null-check <see cref="InputSourceRegistry.Current"/>, and so scenes can be
    /// opened before the real device adapter exists.
    /// </summary>
    public sealed class NullInputSource : IInputSource
    {
        public static readonly NullInputSource Instance = new NullInputSource();

        private NullInputSource()
        {
        }

        public bool IsEnabled => false;
        public Vector2 Move => Vector2.zero;
        public AimSourceKind AimKind => AimSourceKind.Pointer;
        public Vector2 AimScreenPosition => Vector2.zero;
        public Vector2 AimStick => Vector2.zero;
        public bool FireHeld => false;
        public bool FirePressed => false;
        public bool FireReleased => false;
        public bool ReloadPressed => false;
        public bool PausePressed => false;
        public bool DebugPanelPressed => false;

#pragma warning disable 67
        public event Action<AimSourceKind> AimKindChanged;
#pragma warning restore 67

        public void Enable()
        {
        }

        public void Disable()
        {
        }
    }
}
