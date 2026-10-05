#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DD.Prototype.Qa
{
    /// <summary>
    /// WP-026 · A virtual <see cref="Gamepad"/> registered with the Input System. The bot writes
    /// a <see cref="GamepadState"/> once per frame and queues it as a real device event, so the
    /// prototype's own <c>DD_Controls</c> actions (Gamepad scheme: leftStick = Move,
    /// rightStick = AimStick, rightTrigger = Fire, buttonWest = Reload, start = Pause) see it
    /// exactly as they would see a physical pad. No gameplay code is touched and no
    /// <c>IInputSource</c> is swapped: the input travels through <c>PrototypeInputSource</c>.
    /// </summary>
    public sealed class QaVirtualGamepad : IDisposable
    {
        public const string DeviceName = "QaBotGamepad";

        private Gamepad _pad;
        private GamepadState _state;
        private bool _dirty;
        private readonly List<InputDevice> _suspended = new List<InputDevice>();
        private InputSettings.BackgroundBehavior _backgroundBefore;
        private bool _backgroundTaken;

        public Gamepad Device => _pad;
        public bool IsAlive => _pad != null && _pad.added;

        /// <summary>The pad is added and enabled — a disabled pad's events never reach the actions.</summary>
        public bool PadEnabled => _pad != null && _pad.added && _pad.enabled;

        /// <summary>Native keyboards / pointers held off for the bot run.</summary>
        public int SuspendedNative => _suspended.Count;

        /// <summary>
        /// R2 (C-00 01.10, FINAL dab326ab A2 fp "stick 0 / pointer 1587"): with no InputSettings asset the background
        /// behaviour is ResetAndDisableNonBackgroundDevices, so a pad added (or running) while the Player is unfocused is
        /// parked disabled, and one real mouse / key event turns the active scheme to the pointer — either way the bot's input
        /// stops reaching the game while the round goes on. For the bot run: input ignores focus (set before the add, as
        /// Input/Testing/VirtualGamepadDriver does), and native keyboards / pointers — also ones that connect later — are
        /// suspended so the scheme stays on this pad. Dispose gives both back.
        /// </summary>
        public QaVirtualGamepad()
        {
            _backgroundBefore = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            _backgroundTaken = true;
            var devices = new List<InputDevice>(InputSystem.devices);
            for (int i = 0; i < devices.Count; i++) SuspendNative(devices[i]);
            InputSystem.onDeviceChange += OnDeviceChange;
            _pad = InputSystem.AddDevice<Gamepad>(DeviceName);
            _state = new GamepadState();
            _dirty = true;
        }

        /// <summary>Sticks are clamped to the unit disc; the fire trigger is analogue (1 = held).</summary>
        public void Set(Vector2 move, Vector2 aim, bool fire, bool reload, bool pause)
        {
            _state = BuildState(move, aim, fire, reload, pause);
            _dirty = true;
        }

        public static GamepadState BuildState(Vector2 move, Vector2 aim, bool fire, bool reload, bool pause)
        {
            var s = new GamepadState
            {
                leftStick = Vector2.ClampMagnitude(move, 1f),
                rightStick = Vector2.ClampMagnitude(aim, 1f),
                rightTrigger = fire ? 1f : 0f,
                leftTrigger = 0f
            };
            s = s.WithButton(GamepadButton.West, reload);
            s = s.WithButton(GamepadButton.Start, pause);
            return s;
        }

        /// <summary>Queue the current state as a device event; the Input System applies it on its next update.</summary>
        public void Flush()
        {
            if (_pad == null || !_pad.added)
            {
                return;
            }

            InputSystem.QueueStateEvent(_pad, _state);
            _dirty = false;
        }

        public void Neutral()
        {
            Set(Vector2.zero, Vector2.zero, false, false, false);
            Flush();
        }

        private void SuspendNative(InputDevice device)
        {
            if (device == null || !device.native || !device.enabled || !(device is Keyboard || device is Pointer)) return;
            InputSystem.DisableDevice(device);
            if (!_suspended.Contains(device)) _suspended.Add(device);
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected || change == InputDeviceChange.Enabled) SuspendNative(device);
        }

        public void Dispose()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            try
            {
                if (_backgroundTaken) InputSystem.settings.backgroundBehavior = _backgroundBefore;
                for (int i = 0; i < _suspended.Count; i++)
                {
                    if (_suspended[i] != null && _suspended[i].added) InputSystem.EnableDevice(_suspended[i]);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[QaVirtualGamepad] input restore failed: " + e.Message);
            }

            _backgroundTaken = false;
            _suspended.Clear();
            if (_pad != null)
            {
                try
                {
                    if (_pad.added)
                    {
                        InputSystem.QueueStateEvent(_pad, new GamepadState());
                        InputSystem.RemoveDevice(_pad);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[QaVirtualGamepad] remove failed: " + e.Message);
                }

                _pad = null;
            }
        }
    }
}
#endif
