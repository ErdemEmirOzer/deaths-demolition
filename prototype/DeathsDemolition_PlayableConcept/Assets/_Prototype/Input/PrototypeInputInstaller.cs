using DD.Prototype.Core.DebugTools;
using DD.Prototype.Core.InputAbstraction;
using UnityEngine;
using UnityEngine.InputSystem;

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
        private bool _installed;

        private DebugPanelReadout _stateReadout;
        private DebugPanelReadout _deviceReadout;

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

            if (_enableOnAwake)
            {
                _source.Enable();
            }
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
        }

        private void OnDisable()
        {
            DebugPanelRegistry.Remove(_stateReadout);
            DebugPanelRegistry.Remove(_deviceReadout);
            _stateReadout = null;
            _deviceReadout = null;
        }

        private void Update()
        {
            // Heartbeat only. Every consumer already refreshes on read; this exists so
            // AimKindChanged still fires on a frame where nothing polled the source.
            if (_source != null)
            {
                _source.PumpFrame();
            }
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
            return string.Format("{0}   |  gamepad: {1}", _source.LastAimDeviceName, gamepad);
        }
    }
}
