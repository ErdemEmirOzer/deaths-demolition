using System;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DD.Prototype.Inputs
{
    /// <summary>
    /// The one and only concrete <see cref="IInputSource"/>. Wraps
    /// Assets/_Prototype/Input/Resources/DD_Controls.inputactions.
    ///
    /// DESIGN NOTES
    /// ------------
    /// * PULL, NOT PUSH. Every getter calls <see cref="EnsureFresh"/>, which re-reads
    ///   the device state at most once per rendered frame. That removes any script
    ///   execution order dependency: it does not matter whether the player, the weapon,
    ///   the camera or the F1 panel reads first, they all see the same snapshot of the
    ///   same frame. Nothing here needs a fixed Script Execution Order entry, which
    ///   matters because ProjectSettings is off limits for this task.
    ///
    /// * PAUSE SAFE. All timing uses Time.unscaledTime and Time.frameCount, and the
    ///   Input System processes events in the dynamic update regardless of timeScale.
    ///   Values therefore stay live at timeScale == 0 so the pause menu and the debug
    ///   panel keep working.
    ///
    /// * EDGE FLAGS. FirePressed / FireReleased / ReloadPressed / PausePressed /
    ///   DebugPanelPressed come from InputAction.WasPressedThisFrame(), which is a
    ///   non-consuming query evaluated against the input update counter. Reading it
    ///   twice in one frame returns the same answer, so two consumers cannot steal an
    ///   edge from each other.
    ///
    /// * NO GENERATED WRAPPER. The .inputactions asset does not generate C# so that
    ///   Input System types cannot leak out of this folder. Actions are looked up by
    ///   the constants in PrototypeInputNames.
    ///
    /// * The asset is instantiated, not used in place, so enabling maps at runtime
    ///   never leaves the imported asset in an enabled state between play sessions.
    /// </summary>
    public sealed class PrototypeInputSource : IExtendedInputSource, IDisposable
    {
        // ------------------------------------------------------------ tuning values
        // PROTOTYPE-ONLY, invented for this adapter. They are behaviour thresholds of
        // the input adapter itself, not gameplay design numbers, so they live here
        // rather than in PrototypeConfig.

        /// <summary>
        /// PROTOTYPE-ONLY (invented). Screen-pixel distance the pointer must travel in
        /// one frame before it counts as "the player is aiming with the mouse". Stops
        /// sensor noise or a nudged desk from stealing aim away from a gamepad.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None,
            "Screen pixels the pointer must move before it counts as aim intent.", true)]
        public const float PointerIntentThresholdPixels = 1.5f;

        /// <summary>
        /// PROTOTYPE-ONLY (invented). Right-stick magnitude that counts as aim intent.
        /// The binding already applies StickDeadzone(min=0.22), so this is a second,
        /// deliberately tiny guard against a value that squeaks past the deadzone.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None,
            "Right-stick magnitude that counts as aim intent, on top of the software deadzone.", true)]
        public const float StickIntentThreshold = 0.06f;

        /// <summary>
        /// WP-024: outer edge of the radial deadzone. Stick magnitude at or above this
        /// reads as 1; the inner edge comes from <see cref="InputPreferences.StickDeadzone"/>
        /// (default 0.15). The binding-level StickDeadzone processors were removed so a
        /// single, player-adjustable rule applies to both sticks.
        /// </summary>
        [PrototypeOnly(PendingDecisions.None, "Radial deadzone outer edge (saturation).", true)]
        public const float StickSaturation = 0.95f;

        // ---------------------------------------------------------------- plumbing

        private readonly InputActionAsset _asset;
        private readonly bool _ownsAsset;
        private readonly InputActionMap _gameplay;

        private readonly InputAction _move;
        private readonly InputAction _aim;
        private readonly InputAction _aimStick;
        private readonly InputAction _fire;
        private readonly InputAction _reload;
        private readonly InputAction _pause;
        private readonly InputAction _debugPanel;
        private readonly InputAction _special;
        private readonly InputAction _interact;

        private bool _enabled;
        private bool _disposed;

        // --------------------------------------------------------- per-frame cache

        private int _cachedFrame = -1;

        private Vector2 _move01;
        private Vector2 _aimScreen;
        private Vector2 _aimStick01;
        private bool _fireHeld;
        private bool _firePressed;
        private bool _fireReleased;
        private bool _reloadPressed;
        private bool _pausePressed;
        private bool _debugPanelPressed;
        private bool _specialHeld;
        private bool _specialPressed;
        private bool _interactPressed;
        private float _deadzone = 0.15f;

        private AimSourceKind _aimKind = AimSourceKind.Pointer;
        private InputSchemeKind _scheme = InputSchemeKind.KeyboardMouse;
        private float _lastPointerIntentTime = float.NegativeInfinity;
        private float _lastStickIntentTime = float.NegativeInfinity;
        private Vector2 _previousPointerSample;
        private bool _pointerSampled;
        private bool _refreshing;

        /// <summary>Human readable name of the device that last supplied aim intent. Debug only.</summary>
        public string LastAimDeviceName { get; private set; } = "none";

        public PrototypeInputSource(InputActionAsset controls)
        {
            if (controls == null)
            {
                throw new ArgumentNullException(nameof(controls));
            }

            InputActionAsset runtime = controls;
            try
            {
                InputActionAsset clone = UnityEngine.Object.Instantiate(controls);
                if (clone != null)
                {
                    clone.name = controls.name;
                    clone.hideFlags = HideFlags.HideAndDontSave;
                    runtime = clone;
                    _ownsAsset = true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "[Input] Could not clone DD_Controls, using the imported asset directly. " + e.Message);
            }

            _asset = runtime;
            _gameplay = _asset.FindActionMap(PrototypeInputNames.MapGameplay, false);

            if (_gameplay == null)
            {
                Debug.LogError(
                    "[Input] DD_Controls has no action map called '" + PrototypeInputNames.MapGameplay +
                    "'. Input will stay dead.");
                return;
            }

            _move = Find(PrototypeInputNames.ActionMove);
            _aim = Find(PrototypeInputNames.ActionAim);
            _aimStick = Find(PrototypeInputNames.ActionAimStick);
            _fire = Find(PrototypeInputNames.ActionFire);
            _reload = Find(PrototypeInputNames.ActionReload);
            _pause = Find(PrototypeInputNames.ActionPause);
            _debugPanel = Find(PrototypeInputNames.ActionDebugPanel);
            _special = Find(PrototypeInputNames.ActionSpecial);
            _interact = Find(PrototypeInputNames.ActionInteract);
        }

        /// <summary>The runtime (cloned) asset. The remap service works on this instance so overrides apply live.</summary>
        public InputActionAsset Asset => _asset;

        /// <summary>WP-024: apply a radial deadzone with the outer edge at <see cref="StickSaturation"/>. Pure; tests call it.</summary>
        public static Vector2 ApplyRadialDeadzone(Vector2 raw, float deadzone)
        {
            deadzone = Mathf.Clamp(deadzone, 0f, InputPreferences.MaxStickDeadzone);
            float magnitude = raw.magnitude;
            if (magnitude <= deadzone || magnitude <= 1e-6f)
            {
                return Vector2.zero;
            }

            float range = Mathf.Max(0.01f, StickSaturation - deadzone);
            float scaled = Mathf.Clamp01((magnitude - deadzone) / range);
            return raw / magnitude * scaled;
        }

        private InputAction Find(string actionName)
        {
            InputAction action = _gameplay.FindAction(actionName, false);
            if (action == null)
            {
                Debug.LogError(
                    "[Input] DD_Controls/Gameplay is missing the action '" + actionName +
                    "'. That control will do nothing.");
            }

            return action;
        }

        // ------------------------------------------------------------- IInputSource

        public bool IsEnabled
        {
            get { return _enabled && !_disposed; }
        }

        public Vector2 Move
        {
            get { EnsureFresh(); return _move01; }
        }

        public AimSourceKind AimKind
        {
            get { EnsureFresh(); return _aimKind; }
        }

        public Vector2 AimScreenPosition
        {
            get { EnsureFresh(); return _aimScreen; }
        }

        public Vector2 AimStick
        {
            get { EnsureFresh(); return _aimStick01; }
        }

        public bool FireHeld
        {
            get { EnsureFresh(); return _fireHeld; }
        }

        public bool FirePressed
        {
            get { EnsureFresh(); return _firePressed; }
        }

        public bool FireReleased
        {
            get { EnsureFresh(); return _fireReleased; }
        }

        public bool ReloadPressed
        {
            get { EnsureFresh(); return _reloadPressed; }
        }

        public bool PausePressed
        {
            get { EnsureFresh(); return _pausePressed; }
        }

        public bool DebugPanelPressed
        {
            get { EnsureFresh(); return _debugPanelPressed; }
        }

        public bool SpecialHeld
        {
            get { EnsureFresh(); return _specialHeld; }
        }

        public bool SpecialPressed
        {
            get { EnsureFresh(); return _specialPressed; }
        }

        public bool InteractPressed
        {
            get { EnsureFresh(); return _interactPressed; }
        }

        public InputSchemeKind ActiveScheme
        {
            get { EnsureFresh(); return _scheme; }
        }

        public float StickDeadzone
        {
            get { return _deadzone; }
        }

        /// <summary>WP-024: raised when the active control scheme flips (button presses count, unlike AimKindChanged).</summary>
        public event Action<InputSchemeKind> SchemeChanged;

        public event Action<AimSourceKind> AimKindChanged;

        public void Enable()
        {
            if (_disposed || _gameplay == null || _enabled)
            {
                return;
            }

            _gameplay.Enable();
            _enabled = true;
            _cachedFrame = -1;
            _pointerSampled = false;
        }

        public void Disable()
        {
            if (_disposed || _gameplay == null || !_enabled)
            {
                return;
            }

            _gameplay.Disable();
            _enabled = false;
            ClearState();
            _cachedFrame = Time.frameCount;
        }

        /// <summary>
        /// Optional heartbeat. The installer calls it once per frame so that
        /// <see cref="AimKindChanged"/> still fires on a frame where nobody read a
        /// property. Consumers never have to call this.
        /// </summary>
        public void PumpFrame()
        {
            EnsureFresh();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Disable();
            _disposed = true;
            AimKindChanged = null;
            SchemeChanged = null;

            if (_ownsAsset && _asset != null)
            {
                UnityEngine.Object.Destroy(_asset);
            }
        }

        // ---------------------------------------------------------------- refresh

        private void EnsureFresh()
        {
            if (_refreshing)
            {
                return;
            }

            int frame = Time.frameCount;
            if (frame == _cachedFrame)
            {
                return;
            }

            _cachedFrame = frame;

            _refreshing = true;
            try
            {
                Refresh();
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void Refresh()
        {
            if (!_enabled || _disposed)
            {
                ClearState();
                return;
            }

            float now = Time.unscaledTime;
            _deadzone = InputPreferencesStore.Current.StickDeadzone;

            Vector2 moveRaw = _move != null ? _move.ReadValue<Vector2>() : Vector2.zero;
            Vector2 stickRaw = _aimStick != null ? _aimStick.ReadValue<Vector2>() : Vector2.zero;

            // The keyboard composite already yields unit steps; the deadzone only matters
            // for an analogue stick, and a WASD vector never falls inside it.
            _move01 = Vector2.ClampMagnitude(IsFromGamepad(_move) ? ApplyRadialDeadzone(moveRaw, _deadzone) : moveRaw, 1f);
            _aimStick01 = Vector2.ClampMagnitude(ApplyRadialDeadzone(stickRaw, _deadzone), 1f);

            Vector2 pointer = _aim != null ? _aim.ReadValue<Vector2>() : Vector2.zero;
            if (pointer == Vector2.zero && Mouse.current != null)
            {
                pointer = Mouse.current.position.ReadValue();
            }

            _aimScreen = pointer;

            _fireHeld = _fire != null && _fire.IsPressed();
            _firePressed = _fire != null && _fire.WasPressedThisFrame();
            _fireReleased = _fire != null && _fire.WasReleasedThisFrame();
            _reloadPressed = _reload != null && _reload.WasPressedThisFrame();
            _pausePressed = _pause != null && _pause.WasPressedThisFrame();
            _debugPanelPressed = _debugPanel != null && _debugPanel.WasPressedThisFrame();
            _specialHeld = _special != null && _special.IsPressed();
            _specialPressed = _special != null && _special.WasPressedThisFrame();
            _interactPressed = _interact != null && _interact.WasPressedThisFrame();

            ResolveAimKind(now);
            ResolveScheme();
        }

        private static bool IsFromGamepad(InputAction action)
        {
            if (action == null || !action.enabled)
            {
                return false;
            }

            InputControl control = action.activeControl;
            return control != null && control.device is Gamepad;
        }

        /// <summary>
        /// WP-024: the scheme follows the same intent stamps as the aim kind. Any
        /// gamepad actuation (stick, trigger, button) makes Gamepad current; any mouse
        /// or keyboard actuation makes KeyboardMouse current; a tie keeps the incumbent.
        /// </summary>
        private void ResolveScheme()
        {
            InputSchemeKind resolved = _scheme;
            if (_lastStickIntentTime > _lastPointerIntentTime)
            {
                resolved = InputSchemeKind.Gamepad;
            }
            else if (_lastPointerIntentTime > _lastStickIntentTime)
            {
                resolved = InputSchemeKind.KeyboardMouse;
            }

            if (resolved == _scheme)
            {
                return;
            }

            _scheme = resolved;
            Action<InputSchemeKind> handler = SchemeChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke(resolved);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// DEVICE SWITCH RULE
        /// ------------------
        /// Every relevant actuation stamps a timestamp for its device family:
        ///
        ///   pointer family : Mouse, Pointer, Keyboard
        ///   stick   family : Gamepad
        ///
        /// AimKind is whichever family has the newer stamp. A tie - including the very
        /// first frame, when neither has ever been touched - keeps the incumbent, so
        /// the aim source never flickers.
        ///
        /// What counts as an actuation:
        ///   * pointer moved more than PointerIntentThresholdPixels since last frame
        ///   * right stick magnitude above StickIntentThreshold
        ///   * Move / Fire / Reload / Pause in progress, credited to the device of
        ///     their currently active control
        ///
        /// Consequence, stated plainly: pressing W or nudging the mouse pulls aim back
        /// to the cursor, touching any gamepad control pushes it to the right stick.
        /// A resting gamepad stick cannot steal aim, and a resting mouse cannot steal
        /// it back, because neither produces an actuation.
        /// </summary>
        private void ResolveAimKind(float now)
        {
            if (_pointerSampled)
            {
                if ((_aimScreen - _previousPointerSample).sqrMagnitude >
                    PointerIntentThresholdPixels * PointerIntentThresholdPixels)
                {
                    _lastPointerIntentTime = now;
                    LastAimDeviceName = "Mouse";
                }
            }
            else
            {
                _pointerSampled = true;
            }

            _previousPointerSample = _aimScreen;

            if (_aimStick01.sqrMagnitude > StickIntentThreshold * StickIntentThreshold)
            {
                _lastStickIntentTime = now;
                LastAimDeviceName = "Gamepad right stick";
            }

            NoteActionIntent(_move, now);
            NoteActionIntent(_fire, now);
            NoteActionIntent(_reload, now);
            NoteActionIntent(_pause, now);
            NoteActionIntent(_special, now);
            NoteActionIntent(_interact, now);

            AimSourceKind resolved = _aimKind;
            if (_lastStickIntentTime > _lastPointerIntentTime)
            {
                resolved = AimSourceKind.Stick;
            }
            else if (_lastPointerIntentTime > _lastStickIntentTime)
            {
                resolved = AimSourceKind.Pointer;
            }

            if (resolved == _aimKind)
            {
                return;
            }

            _aimKind = resolved;

            Action<AimSourceKind> handler = AimKindChanged;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler.Invoke(resolved);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void NoteActionIntent(InputAction action, float now)
        {
            if (action == null || !action.enabled)
            {
                return;
            }

            if (!action.IsInProgress())
            {
                return;
            }

            InputControl control = action.activeControl;
            if (control == null)
            {
                return;
            }

            InputDevice device = control.device;
            if (device == null)
            {
                return;
            }

            if (device is Gamepad)
            {
                _lastStickIntentTime = now;
                LastAimDeviceName = "Gamepad (" + control.name + ")";
                return;
            }

            if (device is Keyboard || device is Mouse || device is Pointer)
            {
                _lastPointerIntentTime = now;
                LastAimDeviceName = device.displayName;
            }
        }

        private void ClearState()
        {
            _move01 = Vector2.zero;
            _aimStick01 = Vector2.zero;
            _fireHeld = false;
            _firePressed = false;
            _fireReleased = false;
            _reloadPressed = false;
            _pausePressed = false;
            _debugPanelPressed = false;
            _specialHeld = false;
            _specialPressed = false;
            _interactPressed = false;
        }
    }
}
