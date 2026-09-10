using System;
using System.Collections.Generic;
using UnityEngine;

namespace DD.Prototype.Core.DebugTools
{
    /// <summary>Well-known tab names so later tasks land in the same tabs.</summary>
    public static class DebugPanelTabs
    {
        public const string Config = "Config";
        public const string Rounds = "Rounds";
        public const string Enemies = "Enemies";
        public const string Weapons = "Weapons";
        public const string Economy = "Economy";
        public const string Spawn = "Spawn";
        public const string Performance = "Perf";
        public const string Candidates = "Candidates";
        public const string Misc = "Misc";
    }

    public abstract class DebugPanelEntry
    {
        public string Tab = DebugPanelTabs.Misc;
        public string Label = "";
        public int Order;

        /// <summary>Optional gate: the row is hidden while this returns false.</summary>
        public Func<bool> Visible;
    }

    public sealed class DebugPanelButton : DebugPanelEntry
    {
        public Action OnPressed;
    }

    public sealed class DebugPanelToggle : DebugPanelEntry
    {
        public Func<bool> Get;
        public Action<bool> Set;
    }

    public sealed class DebugPanelSlider : DebugPanelEntry
    {
        public Func<float> Get;
        public Action<float> Set;
        public float Min;
        public float Max = 1f;
        public bool WholeNumbers;
    }

    public sealed class DebugPanelReadout : DebugPanelEntry
    {
        public Func<string> Text;
    }

    /// <summary>Escape hatch: draw raw IMGUI inside the panel body.</summary>
    public sealed class DebugPanelSection : DebugPanelEntry
    {
        public Action Draw;
    }

    /// <summary>
    /// Where every other task hangs its debug controls.
    ///
    /// Register in OnEnable, remove in OnDisable, keep the returned handle:
    ///
    ///     _skipRound = DebugPanelRegistry.AddButton(
    ///         DebugPanelTabs.Rounds, "Skip Round", () => _director.ForceCompleteRound());
    ///     ...
    ///     DebugPanelRegistry.Remove(_skipRound);
    ///
    /// The panel itself never has to know what exists.
    /// </summary>
    public static class DebugPanelRegistry
    {
        private static readonly List<DebugPanelEntry> s_entries = new List<DebugPanelEntry>(32);
        private static readonly List<string> s_tabs = new List<string>(8);

        public static IReadOnlyList<DebugPanelEntry> Entries => s_entries;

        /// <summary>Distinct tab names in registration order, Config always first.</summary>
        public static IReadOnlyList<string> Tabs => s_tabs;

        /// <summary>Raised whenever the entry set changes, so the panel can re-sort.</summary>
        public static event Action Changed;

        public static DebugPanelButton AddButton(string tab, string label, Action onPressed, int order = 0)
        {
            return Add(new DebugPanelButton { Tab = tab, Label = label, OnPressed = onPressed, Order = order });
        }

        public static DebugPanelToggle AddToggle(string tab, string label, Func<bool> get, Action<bool> set, int order = 0)
        {
            return Add(new DebugPanelToggle { Tab = tab, Label = label, Get = get, Set = set, Order = order });
        }

        public static DebugPanelSlider AddSlider(string tab, string label, Func<float> get, Action<float> set,
            float min, float max, bool wholeNumbers = false, int order = 0)
        {
            return Add(new DebugPanelSlider
            {
                Tab = tab, Label = label, Get = get, Set = set,
                Min = min, Max = max, WholeNumbers = wholeNumbers, Order = order
            });
        }

        public static DebugPanelReadout AddReadout(string tab, string label, Func<string> text, int order = 0)
        {
            return Add(new DebugPanelReadout { Tab = tab, Label = label, Text = text, Order = order });
        }

        public static DebugPanelSection AddSection(string tab, string label, Action draw, int order = 0)
        {
            return Add(new DebugPanelSection { Tab = tab, Label = label, Draw = draw, Order = order });
        }

        public static T Add<T>(T entry) where T : DebugPanelEntry
        {
            if (entry == null)
            {
                return null;
            }

            if (string.IsNullOrEmpty(entry.Tab))
            {
                entry.Tab = DebugPanelTabs.Misc;
            }

            s_entries.Add(entry);
            RebuildTabs();
            Changed?.Invoke();
            return entry;
        }

        public static void Remove(DebugPanelEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (s_entries.Remove(entry))
            {
                RebuildTabs();
                Changed?.Invoke();
            }
        }

        public static void RemoveAll()
        {
            s_entries.Clear();
            RebuildTabs();
            Changed?.Invoke();
        }

        private static void RebuildTabs()
        {
            s_tabs.Clear();
            s_tabs.Add(DebugPanelTabs.Config);

            for (int i = 0; i < s_entries.Count; i++)
            {
                string tab = s_entries[i].Tab;
                if (!s_tabs.Contains(tab))
                {
                    s_tabs.Add(tab);
                }
            }

            if (!s_tabs.Contains(DebugPanelTabs.Candidates))
            {
                s_tabs.Add(DebugPanelTabs.Candidates);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_entries.Clear();
            s_tabs.Clear();
            Changed = null;
            RebuildTabs();
        }
    }
}
