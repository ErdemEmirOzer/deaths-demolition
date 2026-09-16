using DD.Prototype.Core.DebugTools;
using DD.Prototype.Core.InputAbstraction;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace DD.Prototype.Inputs
{
    /// <summary>
    /// The persistent object that owns the concrete input source and publishes it on
    /// <see cref="InputSourceRegistry"/>.
    ///
    /// It installs itself after the first scene load, so input works in every scene
    /// without anybody having to remember to drag a prefab in. Dropping the component
    /// into a scene by hand also works and takes priority (the auto-installed copy
    /// destroys itself if it finds an existing one).
    ///
    /// The control asset is loaded from
    /// Assets/_Prototype/Input/Resources/DD_Controls.inputactions. It lives under a
    /// Resources folder precisely so that no scene wiring is required; the folder is
    /// inside the Input folder so the "no Input System outside Assets/_Prototype/Input"
    /// rule still holds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrototypeInputInstaller : MonoBehaviour
    {
        /// <summary>Resources-relative path of the control asset.</summary>
        public const string ControlsResourcePath = "DD_Controls";

        private const string RootName = "[Prototype Input]";

        public static PrototypeInputInstaller Instance { get; private set; }

        [Tooltip("Optional. Leave empty to load DD_Controls from Resources.")]
        [SerializeField] private InputActionAsset _controlsOverride = null;

        [Tooltip("Enable the Gameplay map as soon as the source is installed.")]
        [SerializeField] private bool _enableOnAwake = true;

        private PrototypeInputSource _source;
        private InputRemapService _remap;
        private GamepadRumble _rumble;
        private EventSystem _bridgedEventSystem;
        private bool _installed;
        private int _rebindSuspendDepth;

        private DebugPanelReadout _stateReadout;
        private DebugPanelReadout _deviceReadout;
        private DebugPanelReadout _prefsReadout;

        /// <summary>WP-024: the live remap service (null before installation).</summary>
        public IInputRemapService Remap => _remap;

        /// <summary>WP-024: the rumble service (null before installation).</summary>
        public GamepadRumble Rumble => _rumble;

        /// <summary>WP-024: true once the EventSystem runs on the Input System UI module (UI map).</summary>
        public bool UiModuleBridged => _bridgedEventSystem != null;

        /// <summary>The live source, or null before installation.</summary>
        public IInputSource Source
        {
            get { return _source; }
        }

        /// <summary>Create the installer if it does not exist yet. Idempotent.</summary>
        public static PrototypeInputInstaller EnsureExists()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject(RootName);
            DontDestroyOnLoad(go);
            return go.AddComponent<PrototypeInputInstaller>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            EnsureExists();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            InputActionAsset controls = _controlsOverride != null
                ? _controlsOverride
                : Resources.Load<InputActionAsset>(ControlsResourcePath);

            if (controls == null)
            {
                Debug.LogError(
                    "[Input] DD_Controls could not be loaded from Resources/" + ControlsResourcePath +
                    ". Expected Assets/_Prototype/Input/Resources/DD_Controls.inputactions. " +
                    "The prototype will run with a dead input source.");
                return;
            }

            _source = new PrototypeInputSource(controls);
            InputSourceRegistry.SetCurrent(_source);
            _installed = true;

            // WP-024: overrides are loaded before the map is enabled so the first frame
            // already runs on the player's bindings; preferences (deadzone etc.) load
            // lazily on first read.
            _remap = new InputRemapService(_source.Asset, SuspendGameplayForRebind);
            _remap.Load();
            InputRemapRegistry.SetCurrent(_remap);

            _rumble = new GamepadRumble();
            _rumble.Subscribe();

            if (_enableOnAwake)
            {
                _source.Enable();
            }
        }

        /// <summary>WP-024: gameplay input is suspended while a rebind listens (nested-safe).</summary>
        private void SuspendGameplayForRebind(bool suspend)
        {
            if (_source == null)
            {
                return;
            }

            _rebindSuspendDepth = Mathf.Max(0, _rebindSuspendDepth + (suspend ? 1 : -1));
            if (_rebindSuspendDepth > 0)
            {
                _source.Disable();
                _rumble?.StopMotors();
            }
            else
            {
                _source.Enable();
            }
        }

        /// <summary>
        /// WP-024: the UI screens are built on a StandaloneInputModule (UiFactory keeps the
        /// UI assembly free of Input System types). This swaps the EventSystem onto the
        /// Input System UI module bound to the UI map — Navigate / Submit / Cancel /
        /// Point / Click / ScrollWheel — so keyboard, mouse and gamepad all drive menus
        /// through one path. Idempotent; re-checked when the EventSystem instance changes.
        /// </summary>
        private void EnsureUiInputModule()
        {
            EventSystem current = EventSystem.current;
            if (current == null || ReferenceEquals(current, _bridgedEventSystem) || _source == null)
            {
                return;
            }

            InputActionMap ui = _source.Asset.FindActionMap(PrototypeInputNames.MapUI, false);
            if (ui == null)
            {
                _bridgedEventSystem = current;
                return;
            }

            InputSystemUIInputModule module = current.GetComponent<InputSystemUIInputModule>();
            if (module == null)
            {
                module = current.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            module.actionsAsset = _source.Asset;
            module.point = Reference(ui, PrototypeInputNames.UiPoint);
            module.leftClick = Reference(ui, PrototypeInputNames.UiClick);
            module.move = Reference(ui, PrototypeInputNames.UiNavigate);
            module.submit = Reference(ui, PrototypeInputNames.UiSubmit);
            module.cancel = Reference(ui, PrototypeInputNames.UiCancel);
            module.scrollWheel = Reference(ui, PrototypeInputNames.UiScrollWheel);
            module.middleClick = null;
            module.rightClick = null;

            StandaloneInputModule legacy = current.GetComponent<StandaloneInputModule>();
            if (legacy != null)
            {
                Destroy(legacy);
            }

            if (!ui.enabled)
            {
                ui.Enable();
            }

            _bridgedEventSystem = current;
        }

        private static InputActionReference Reference(InputActionMap map, string actionName)
        {
            InputAction action = map.FindAction(actionName, false);
            if (action == null)
            {
                return null;
            }

            InputActionReference reference = InputActionReference.Create(action);
            reference.hideFlags = HideFlags.HideAndDontSave;
            return reference;
        }

        private void OnEnable()
        {
            if (Instance != this)
            {
                return;
            }

            _stateReadout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Misc, "Input", DescribeState, -60);

            _deviceReadout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Misc, "Aim device", DescribeDevice, -59);

            _prefsReadout = DebugPanelRegistry.AddReadout(
                DebugPanelTabs.Misc, "Input prefs", DescribePreferences, -58);
        }

        private void OnDisable()
        {
            DebugPanelRegistry.Remove(_stateReadout);
            DebugPanelRegistry.Remove(_deviceReadout);
            DebugPanelRegistry.Remove(_prefsReadout);
            _stateReadout = null;
            _deviceReadout = null;
            _prefsReadout = null;
        }

        private void Update()
        {
            // Heartbeat only. Every consumer already refreshes on read; this exists so
            // AimKindChanged still fires on a frame where nothing polled the source.
            if (_source != null)
            {
                _source.PumpFrame();
            }

            EnsureUiInputModule();
            _rumble?.Tick();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Instance = null;

            if (!_installed)
            {
                return;
            }

            if (ReferenceEquals(InputSourceRegistry.Current, _source))
            {
                InputSourceRegistry.Clear();
            }

            if (ReferenceEquals(InputRemapRegistry.Current, _remap))
            {
                InputRemapRegistry.SetCurrent(null);
            }

            _rumble?.Dispose();
            _rumble = null;
            _remap?.Dispose();
            _remap = null;

            if (_source != null)
            {
                _source.Dispose();
                _source = null;
            }
        }

        /// <summary>Suspend input, e.g. during a scene transition. Pause must NOT use this.</summary>
        public void SetInputEnabled(bool value)
        {
            if (_source == null)
            {
                return;
            }

            if (value)
            {
                _source.Enable();
            }
            else
            {
                _source.Disable();
            }
        }

        private string DescribeState()
        {
            IInputSource s = InputSourceRegistry.Current;
            if (s == null || !s.IsEnabled)
            {
                return "disabled";
            }

            Vector2 m = s.Move;
            return string.Format(
                "move ({0:0.00}, {1:0.00})  aim {2}  fire {3}  paused {4}",
                m.x, m.y, s.AimKind, s.FireHeld ? "held" : "-",
                Mathf.Approximately(Time.timeScale, 0f) ? "yes" : "no");
        }

        private string DescribeDevice()
        {
            if (_source == null)
            {
                return "no source";
            }

            string gamepad = Gamepad.current != null ? Gamepad.current.displayName : "none connected";
            return string.Format("{0}   |  gamepad: {1}  |  scheme: {2}", _source.LastAimDeviceName, gamepad, _source.ActiveScheme);
        }

        private string DescribePreferences()
        {
            InputPreferences p = InputPreferencesStore.Current;
            int overrides = 0;
            if (_remap != null)
            {
                for (int i = 0; i < _remap.Entries.Count; i++)
                {
                    if (_remap.Entries[i].IsOverridden) overrides++;
                }
            }

            return string.Format(
                "deadzone {0:0.00}  rumble {1} x{2:0.00} (pulses {3})  assist {4} {5:0}deg/{6:0}m  overrides {7}  ui-module {8}",
                p.StickDeadzone, p.RumbleEnabled ? "on" : "off", p.RumbleIntensity, _rumble != null ? _rumble.PulseCount : 0,
                p.AimAssistEnabled ? "on" : "off", p.AimAssistAngleDegrees, p.AimAssistDistance, overrides,
                UiModuleBridged ? "InputSystem" : "legacy");
        }
    }
}
