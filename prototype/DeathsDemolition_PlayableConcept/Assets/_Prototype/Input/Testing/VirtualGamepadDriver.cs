#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DD.Prototype.Inputs.Testing
{
    /// <summary>
    /// WP-024 test seam: a virtual gamepad (and, on demand, a virtual keyboard / mouse)
    /// driven through the Input System's event queue, so PlayMode tests and probes can
    /// exercise the real <see cref="PrototypeInputSource"/> without hardware. Lives in
    /// the Input folder so no Input System type is needed in the test assembly.
    /// Editor / development builds only.
    /// </summary>
    public sealed class VirtualGamepadDriver : IDisposable
    {
        private Gamepad _pad;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private GamepadState _state;
        private bool _disposed;

        public static VirtualGamepadDriver Create()
        {
            var d = new VirtualGamepadDriver();
            d._pad = InputSystem.AddDevice<Gamepad>("DD Virtual Gamepad");
            InputSystem.Update();
            return d;
        }

        /// <summary>
        /// In the Editor, keyboard and pointer events only reach the game while the Game
        /// view has focus (a batchmode test has none); gamepads are exempt. For ONE update
        /// this routes every device to the game — with the physical keyboards / pointers
        /// disabled for that update, so a stray keystroke from the developer can never
        /// leak into the test — then restores the setting. The virtual device's state
        /// persists after the update, so a held key keeps driving its action.
        /// </summary>
        private static void WithVirtualKeyboardPointerRouting(Action queueAndUpdate)
        {
#if UNITY_EDITOR
            InputSettings.EditorInputBehaviorInPlayMode previous = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSettings.BackgroundBehavior previousBackground = InputSystem.settings.backgroundBehavior;
            var suspended = new System.Collections.Generic.List<InputDevice>();
            foreach (InputDevice device in InputSystem.devices)
            {
                if (device.native && device.enabled && (device is Keyboard || device is Pointer))
                {
                    InputSystem.DisableDevice(device);
                    suspended.Add(device);
                }
            }

            // A keyboard / pointer ADDED while the game is unfocused is parked as
            // "disabled while in background"; IgnoreFocus during the add keeps it live.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            try
            {
                queueAndUpdate();
            }
            finally
            {
                InputSystem.settings.editorInputBehaviorInPlayMode = previous;
                InputSystem.settings.backgroundBehavior = previousBackground;
                for (int i = 0; i < suspended.Count; i++)
                {
                    InputSystem.EnableDevice(suspended[i]);
                }
            }
#else
            queueAndUpdate();
#endif
        }

        public bool IsConnected => _pad != null && _pad.added;

        public string DeviceName => _pad != null ? _pad.displayName : "none";

        /// <summary>
        /// Queue a full gamepad state. Unset buttons are released. The event is processed by
        /// the next dynamic input update (the following frame), so edge flags
        /// (WasPressedThisFrame) line up with the consumers' Update; pass
        /// <paramref name="processNow"/> to flush immediately for level reads.
        /// </summary>
        public void Set(Vector2 leftStick, Vector2 rightStick, bool fire = false, bool special = false,
            bool start = false, bool south = false, bool west = false, bool east = false, bool select = false,
            Vector2 dpad = default, bool processNow = false, bool focus = false)
        {
            if (_disposed || _pad == null)
            {
                return;
            }

            _state = new GamepadState
            {
                leftStick = leftStick,
                rightStick = rightStick,
                rightTrigger = fire ? 1f : 0f,
                leftTrigger = focus ? 1f : 0f,   // WP-069 B2: LT = Focus / ADS; the Special moved to LB
            };

            if (special) _state = _state.WithButton(GamepadButton.LeftShoulder);

            if (start) _state = _state.WithButton(GamepadButton.Start);
            if (south) _state = _state.WithButton(GamepadButton.South);
            if (west) _state = _state.WithButton(GamepadButton.West);
            if (east) _state = _state.WithButton(GamepadButton.East);
            if (select) _state = _state.WithButton(GamepadButton.Select);
            if (dpad.y > 0.5f) _state = _state.WithButton(GamepadButton.DpadUp);
            if (dpad.y < -0.5f) _state = _state.WithButton(GamepadButton.DpadDown);
            if (dpad.x < -0.5f) _state = _state.WithButton(GamepadButton.DpadLeft);
            if (dpad.x > 0.5f) _state = _state.WithButton(GamepadButton.DpadRight);

            InputSystem.QueueStateEvent(_pad, _state);
            if (processNow)
            {
                InputSystem.Update();
            }
        }

        /// <summary>Release everything.</summary>
        public void Release()
        {
            Set(Vector2.zero, Vector2.zero);
        }

        /// <summary>
        /// Press one key on a virtual keyboard (created on first use). <paramref name="keyName"/>
        /// is the Input System <c>Key</c> enum name ("W", "Escape", "R"); null/empty releases all.
        /// Returns false for an unknown name.
        /// </summary>
        public bool KeyDown(string keyName)
        {
            if (_disposed)
            {
                return false;
            }

            Key key = Key.None;
            if (!string.IsNullOrEmpty(keyName) && !Enum.TryParse(keyName, true, out key))
            {
                return false;
            }

            KeyboardState state = key == Key.None ? new KeyboardState() : new KeyboardState(key);
            WithVirtualKeyboardPointerRouting(() =>
            {
                if (_keyboard == null)
                {
                    _keyboard = InputSystem.AddDevice<Keyboard>("DD Virtual Keyboard");
                }

                InputSystem.QueueStateEvent(_keyboard, state);
                InputSystem.Update();
            });
            return true;
        }

        /// <summary>Move a virtual mouse (created on first use) to a screen position.</summary>
        public void MouseMove(Vector2 position, bool leftButton = false)
        {
            if (_disposed)
            {
                return;
            }

            var ms = new MouseState { position = position };
            if (leftButton)
            {
                ms = ms.WithButton(MouseButton.Left);
            }

            WithVirtualKeyboardPointerRouting(() =>
            {
                if (_mouse == null)
                {
                    _mouse = InputSystem.AddDevice<Mouse>("DD Virtual Mouse");
                }

                InputSystem.QueueStateEvent(_mouse, ms);
                InputSystem.Update();
            });
        }

        /// <summary>Whether the platform gamepad is the current one (rumble goes to Gamepad.current).</summary>
        public bool IsCurrent => _pad != null && ReferenceEquals(Gamepad.current, _pad);

        public void MakeCurrent()
        {
            _pad?.MakeCurrent();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_pad != null && _pad.added) InputSystem.RemoveDevice(_pad);
            if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
            if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
            _pad = null;
            _keyboard = null;
            _mouse = null;
            InputSystem.Update();
        }
    }
}
#endif
