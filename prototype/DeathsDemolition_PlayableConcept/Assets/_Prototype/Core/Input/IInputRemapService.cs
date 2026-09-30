using System;
using System.Collections.Generic;

namespace DD.Prototype.Core.InputAbstraction
{
    /// <summary>One rebindable slot: an action's binding for one control scheme.</summary>
    public readonly struct InputRemapEntry
    {
        public InputRemapEntry(string id, string map, string action, InputSchemeKind scheme, string compositePart,
            string defaultPath, string effectivePath, string displayName, bool isOverridden, string defaultDisplayName = null)
        {
            Id = id;
            Map = map;
            Action = action;
            Scheme = scheme;
            CompositePart = compositePart;
            DefaultPath = defaultPath;
            EffectivePath = effectivePath;
            DisplayName = displayName;
            IsOverridden = isOverridden;
            // Not overridden: the effective control IS the default, so its name is the default's name.
            DefaultDisplayName = defaultDisplayName ?? (isOverridden ? string.Empty : displayName);
        }

        /// <summary>Stable identity of the binding (the asset's binding GUID). Survives reordering.</summary>
        public string Id { get; }

        /// <summary>Action map name ("Gameplay" / "UI").</summary>
        public string Map { get; }

        /// <summary>Action name, see <see cref="PrototypeInputNames"/>.</summary>
        public string Action { get; }

        public InputSchemeKind Scheme { get; }

        /// <summary>For a composite (WASD) the part name ("up", "left" ...); empty otherwise.</summary>
        public string CompositePart { get; }

        /// <summary>Control path as authored in the asset, e.g. "&lt;Gamepad&gt;/rightTrigger".</summary>
        public string DefaultPath { get; }

        /// <summary>Control path currently in effect (the override, or the default).</summary>
        public string EffectivePath { get; }

        /// <summary>Human-readable control name for the settings screen ("Right Trigger", "R").</summary>
        public string DisplayName { get; }

        /// <summary>
        /// WP-069 B5 (S-09 WP-068 dilim 9 T3(d), C-00 30.09): the human-readable name of <see cref="DefaultPath"/> for the
        /// settings row's "default: X" — the UI may not turn control paths into names itself (ADR-024-2).
        /// </summary>
        public string DefaultDisplayName { get; }

        public bool IsOverridden { get; }
    }

    /// <summary>Outcome of an interactive rebind.</summary>
    public enum InputRebindResult
    {
        Completed = 0,
        Cancelled = 1,
        Failed = 2
    }

    /// <summary>
    /// WP-024 (ADR-024-2): the API the settings screen (S-09) uses to remap controls.
    /// Implemented inside Assets/_Prototype/Input on top of the Input System's
    /// interactive rebinding; overrides are persisted as JSON in
    /// <c>Application.persistentDataPath/input-overrides.json</c>, separate from the
    /// profile save. No Input System type crosses this interface.
    /// </summary>
    public interface IInputRemapService
    {
        /// <summary>Every rebindable slot of the Gameplay map (both schemes). Snapshot; re-read after a change.</summary>
        IReadOnlyList<InputRemapEntry> Entries { get; }

        /// <summary>True while an interactive rebind is listening for a control.</summary>
        bool IsRebinding { get; }

        /// <summary>Full path of the overrides file (readout).</summary>
        string OverridesFilePath { get; }

        /// <summary>Raised after any binding change (rebind, reset, load).</summary>
        event Action BindingsChanged;

        /// <summary>
        /// Listen for the next control of the entry's scheme and bind it. Escape / the
        /// scheme's cancel control aborts. Gameplay input is suspended while listening.
        /// Returns false when the entry is unknown or a rebind is already running.
        /// </summary>
        bool StartRebind(string entryId, Action<InputRebindResult, InputRemapEntry> onFinished);

        void CancelRebind();

        /// <summary>Programmatic override (settings import, tests). Path in Input System control-path syntax.</summary>
        bool ApplyOverride(string entryId, string controlPath);

        /// <summary>Remove the override of one slot.</summary>
        void ResetBinding(string entryId);

        /// <summary>Remove every override and persist the empty set.</summary>
        void ResetToDefaults();

        /// <summary>Persist the current overrides to <see cref="OverridesFilePath"/>.</summary>
        void Save();

        /// <summary>Re-read the overrides file. A missing or malformed file leaves the defaults.</summary>
        void Load();

        /// <summary>The overrides as JSON (the Input System's own format). Empty set = "{}".</summary>
        string ExportOverridesJson();

        /// <summary>Replace the overrides from JSON produced by <see cref="ExportOverridesJson"/>.</summary>
        void ImportOverridesJson(string json);

        /// <summary>Entry lookup helper. Default when unknown.</summary>
        InputRemapEntry Find(string entryId);
    }

    /// <summary>Where the settings screen finds the live remap service. Null before the installer created it.</summary>
    public static class InputRemapRegistry
    {
        private static IInputRemapService s_current;

        public static IInputRemapService Current => s_current;

        public static event Action<IInputRemapService> CurrentChanged;

        public static void SetCurrent(IInputRemapService service)
        {
            if (ReferenceEquals(service, s_current))
            {
                return;
            }

            s_current = service;
            CurrentChanged?.Invoke(service);
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = null;
            CurrentChanged = null;
        }
    }
}
