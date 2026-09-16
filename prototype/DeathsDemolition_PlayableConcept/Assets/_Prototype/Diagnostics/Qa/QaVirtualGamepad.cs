#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
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

        public Gamepad Device => _pad;
        public bool IsAlive => _pad != null && _pad.added;

        public QaVirtualGamepad()
        {
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

        public void Dispose()
        {
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
