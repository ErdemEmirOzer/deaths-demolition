using System;
using System.Collections.Generic;
using System.IO;
using DD.Prototype.Core.InputAbstraction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DD.Prototype.Inputs
{
    /// <summary>
    /// WP-024 (ADR-024-2): interactive rebinding on top of the Input System, exposed
    /// to the settings screen through <see cref="IInputRemapService"/> (no Input System
    /// type leaves this folder). Overrides live in
    /// <c>Application.persistentDataPath/input-overrides.json</c> — the Input System's
    /// own <c>SaveBindingOverridesAsJson</c> format — and never in the profile save.
    ///
    /// What is rebindable: every non-composite binding of the Gameplay map plus the
    /// parts of the WASD composite, per scheme. The Arrows composite and the DebugPanel
    /// action are not offered (fixed alternates / developer control). The UI map is not
    /// rebindable in this package.
    /// </summary>
    public sealed class InputRemapService : IInputRemapService, IDisposable
    {
        public const string FileName = "input-overrides.json";

        private static readonly HashSet<string> s_hidden = new HashSet<string>(StringComparer.Ordinal)
        {
            PrototypeInputNames.ActionDebugPanel,
            PrototypeInputNames.ActionAim   // pointer position is not a "control" a player rebinds
        };

        private readonly InputActionAsset _asset;
        private readonly InputActionMap _gameplay;
        private readonly string _pathOverride;
        private readonly Action<bool> _suspendGameplay;
        private readonly List<InputRemapEntry> _entries = new List<InputRemapEntry>(32);
        private readonly Dictionary<string, InputAction> _actionById = new Dictionary<string, InputAction>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _bindingIndexById = new Dictionary<string, int>(StringComparer.Ordinal);

        private InputActionRebindingExtensions.RebindingOperation _operation;
        private bool _disposed;

        public event Action BindingsChanged;

        /// <param name="asset">The RUNTIME asset (the adapter's clone) so overrides apply to live actions.</param>
        /// <param name="suspendGameplay">Called with true while listening, false afterwards (installer disables the Gameplay map).</param>
        /// <param name="pathOverride">Test seam: overrides file location.</param>
        public InputRemapService(InputActionAsset asset, Action<bool> suspendGameplay = null, string pathOverride = null)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _gameplay = asset.FindActionMap(PrototypeInputNames.MapGameplay, false);
            _suspendGameplay = suspendGameplay;
            _pathOverride = pathOverride;
            RebuildEntries();
        }

        /// <summary>Test seam: a service on a fresh clone of the shipped DD_Controls asset (no devices needed).</summary>
        public static IInputRemapService CreateFromResources(string pathOverride)
        {
            var controls = Resources.Load<InputActionAsset>(PrototypeInputInstaller.ControlsResourcePath);
            if (controls == null)
            {
                return null;
            }

            InputActionAsset clone = UnityEngine.Object.Instantiate(controls);
            clone.name = controls.name;
            clone.hideFlags = HideFlags.HideAndDontSave;
            return new InputRemapService(clone, null, pathOverride);
        }

        public IReadOnlyList<InputRemapEntry> Entries => _entries;

        public bool IsRebinding => _operation != null && _operation.started && !_operation.completed && !_operation.canceled;

        public string OverridesFilePath => _pathOverride ?? Path.Combine(Application.persistentDataPath, FileName);

        public InputRemapEntry Find(string entryId)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id == entryId)
                {
                    return _entries[i];
                }
            }

            return default;
        }

        public bool StartRebind(string entryId, Action<InputRebindResult, InputRemapEntry> onFinished)
        {
            if (_disposed || IsRebinding || !_actionById.TryGetValue(entryId, out InputAction action) ||
                !_bindingIndexById.TryGetValue(entryId, out int bindingIndex))
            {
                return false;
            }

            InputRemapEntry entry = Find(entryId);
            bool gamepad = entry.Scheme == InputSchemeKind.Gamepad;

            _suspendGameplay?.Invoke(true);
            bool wasEnabled = action.enabled;
            action.Disable();

            _operation = action.PerformInteractiveRebinding(bindingIndex)
                .WithControlsHavingToMatchPath(gamepad ? "<Gamepad>" : "<Keyboard>")
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithControlsExcluding("<Pointer>/position")
                .WithCancelingThrough(gamepad ? "<Gamepad>/start" : "<Keyboard>/escape")
                .WithMatchingEventsBeingSuppressed()
                .OnMatchWaitForAnother(0.05f)
                .OnComplete(op => FinishRebind(op, action, wasEnabled, entryId, InputRebindResult.Completed, onFinished))
                .OnCancel(op => FinishRebind(op, action, wasEnabled, entryId, InputRebindResult.Cancelled, onFinished));

            if (!gamepad)
            {
                // Mouse buttons are legitimate keyboard-scheme controls (Fire = left button).
                _operation.WithControlsHavingToMatchPath("<Mouse>");
            }

            _operation.Start();
            return true;
        }

        private void FinishRebind(InputActionRebindingExtensions.RebindingOperation op, InputAction action, bool wasEnabled,
            string entryId, InputRebindResult result, Action<InputRebindResult, InputRemapEntry> onFinished)
        {
            op.Dispose();
            if (ReferenceEquals(_operation, op))
            {
                _operation = null;
            }

            if (wasEnabled)
            {
                action.Enable();
            }

            _suspendGameplay?.Invoke(false);

            if (result == InputRebindResult.Completed)
            {
                Save();
            }

            RebuildEntries();
            RaiseChanged();
            onFinished?.Invoke(result, Find(entryId));
        }

        public void CancelRebind()
        {
            if (_operation != null && !_operation.completed && !_operation.canceled)
            {
                _operation.Cancel();
            }
        }

        public bool ApplyOverride(string entryId, string controlPath)
        {
            if (_disposed || string.IsNullOrEmpty(controlPath) ||
                !_actionById.TryGetValue(entryId, out InputAction action) ||
                !_bindingIndexById.TryGetValue(entryId, out int bindingIndex))
            {
                return false;
            }

            action.ApplyBindingOverride(bindingIndex, controlPath);
            RebuildEntries();
            RaiseChanged();
            return true;
        }

        public void ResetBinding(string entryId)
        {
            if (_disposed || !_actionById.TryGetValue(entryId, out InputAction action) ||
                !_bindingIndexById.TryGetValue(entryId, out int bindingIndex))
            {
                return;
            }

            action.RemoveBindingOverride(bindingIndex);
            RebuildEntries();
            RaiseChanged();
        }

        public void ResetToDefaults()
        {
            if (_disposed)
            {
                return;
            }

            _asset.RemoveAllBindingOverrides();
            Save();
            RebuildEntries();
            RaiseChanged();
        }

        public void Save()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                string path = OverridesFilePath;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, ExportOverridesJson());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Input] input-overrides.json could not be written. " + e.Message);
            }
        }

        public void Load()
        {
            if (_disposed)
            {
                return;
            }

            string json = null;
            try
            {
                string path = OverridesFilePath;
                if (File.Exists(path))
                {
                    json = File.ReadAllText(path);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Input] input-overrides.json could not be read; keeping defaults. " + e.Message);
            }

            ImportOverridesJson(json);
        }

        public string ExportOverridesJson()
        {
            return _disposed ? "{}" : _asset.SaveBindingOverridesAsJson();
        }

        public void ImportOverridesJson(string json)
        {
            if (_disposed)
            {
                return;
            }

            _asset.RemoveAllBindingOverrides();
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    _asset.LoadBindingOverridesFromJson(json, true);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Input] input-overrides.json is malformed; defaults kept. " + e.Message);
                    _asset.RemoveAllBindingOverrides();
                }
            }

            RebuildEntries();
            RaiseChanged();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CancelRebind();
            _disposed = true;
            BindingsChanged = null;
        }

        // ---------------------------------------------------------------- entries

        private void RebuildEntries()
        {
            _entries.Clear();
            _actionById.Clear();
            _bindingIndexById.Clear();
            if (_gameplay == null)
            {
                return;
            }

            foreach (InputAction action in _gameplay.actions)
            {
                if (s_hidden.Contains(action.name))
                {
                    continue;
                }

                var bindings = action.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    InputBinding b = bindings[i];
                    if (b.isComposite)
                    {
                        continue;
                    }

                    // Only the WASD composite parts are offered; the arrow alternates stay fixed.
                    if (b.isPartOfComposite && !IsWasdPart(bindings, i))
                    {
                        continue;
                    }

                    InputSchemeKind scheme = SchemeOf(b);
                    string id = b.id.ToString();
                    string effective = string.IsNullOrEmpty(b.overridePath) ? b.path : b.overridePath;
                    bool overridden = !string.IsNullOrEmpty(b.overridePath);
                    string display = InputControlPath.ToHumanReadableString(effective,
                        InputControlPath.HumanReadableStringOptions.OmitDevice);

                    _entries.Add(new InputRemapEntry(id, _gameplay.name, action.name, scheme,
                        b.isPartOfComposite ? b.name : string.Empty, b.path, effective, display, overridden));
                    _actionById[id] = action;
                    _bindingIndexById[id] = i;
                }
            }
        }

        private static bool IsWasdPart(UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputBinding> bindings, int partIndex)
        {
            for (int j = partIndex - 1; j >= 0; j--)
            {
                if (bindings[j].isComposite)
                {
                    return string.Equals(bindings[j].name, "WASD", StringComparison.OrdinalIgnoreCase);
                }
            }

            return false;
        }

        private static InputSchemeKind SchemeOf(InputBinding b)
        {
            if (!string.IsNullOrEmpty(b.groups) && b.groups.Contains(PrototypeInputNames.SchemeGamepad))
            {
                return InputSchemeKind.Gamepad;
            }

            return b.path != null && b.path.StartsWith("<Gamepad>", StringComparison.Ordinal)
                ? InputSchemeKind.Gamepad
                : InputSchemeKind.KeyboardMouse;
        }

        private void RaiseChanged()
        {
            Action h = BindingsChanged;
            if (h == null)
            {
                return;
            }

            try
            {
                h.Invoke();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
