using System;
using System.Collections.Generic;
using System.Globalization;
using DD.Prototype.Core.Config;
using DD.Prototype.Core.InputAbstraction;
using DD.Prototype.Core.Pooling;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DD.Prototype.Core.DebugTools
{
    /// <summary>
    /// F1 prototype debug panel.
    ///
    /// Hidden during normal play, costs nothing while closed (no OnGUI work, no
    /// reflection). While open it lists every PROTOTYPE-ONLY value the index can reach -
    /// the PrototypeConfig asset and every per-system tuning object outside it - together
    /// with the pending planning decision that value temporarily occupies. Edits are
    /// applied live through PrototypeConfig.NotifyChanged.
    ///
    /// The index is built when the panel opens and on the Rescan button, never per frame.
    ///
    /// Other systems add their own controls through <see cref="DebugPanelRegistry"/>;
    /// this class never needs to know they exist.
    ///
    /// IMGUI is used on purpose: it needs no prefab, no canvas and no font asset, so
    /// the panel cannot be broken by UI work happening in parallel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrototypeDebugPanel : MonoBehaviour
    {
        private const string RootName = "[Prototype Debug Panel]";

        public static PrototypeDebugPanel Instance { get; private set; }

        [SerializeField] private bool _startVisible;
        [SerializeField] private bool _showOriginTags = true;

        private bool _visible;
        private Rect _window = new Rect(20f, 20f, 760f, 640f);
        private Vector2 _scroll;
        private string _search = string.Empty;
        private string _activeTab = DebugPanelTabs.Config;

        private PrototypeConfig _indexedConfig;
        private ConfigFieldIndexReport _index;
        private bool _showUnbound;
        private readonly List<string> _tabBuffer = new List<string>(8);

        private GUIStyle _headerStyle;
        private GUIStyle _originStyle;
        private GUIStyle _inventedStyle;
        private GUIStyle _rowLabelStyle;
        private GUIStyle _groupStyle;
        private GUIStyle _moduleStyle;
        private bool _stylesReady;

        /// <summary>Open state. Systems may read this to suppress their own hotkeys.</summary>
        public bool IsVisible => _visible;

        /// <summary>Create the panel if it does not exist yet. Called by the bootstrapper.</summary>
        public static PrototypeDebugPanel EnsureExists()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var go = new GameObject(RootName);
            DontDestroyOnLoad(go);
            return go.AddComponent<PrototypeDebugPanel>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _visible = _startVisible && UnityEngine.Debug.isDebugBuild;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            // WP-112 (C-00 12.10, presentable release): the F1 / pad panel ("PROTOTYPE DEBUG") opens in development builds and
            // the Editor only; a release build never draws it.
            PrototypeConfig config = PrototypeConfig.Active;
            if (!UnityEngine.Debug.isDebugBuild || (config != null && !config.System.DebugPanelEnabled))
            {
                _visible = false;
                return;
            }

            if (TogglePressedThisFrame())
            {
                SetVisible(!_visible);
            }
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (visible)
            {
                RebuildIndex();
            }
        }

        private static bool TogglePressedThisFrame()
        {
            if (InputSourceRegistry.HasRealSource)
            {
                return InputSourceRegistry.Current.DebugPanelPressed;
            }

#if ENABLE_INPUT_SYSTEM
            // Fallback so the panel works before the real input adapter is installed.
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.f1Key.wasPressedThisFrame;
#else
            return false;
#endif
        }

        private void RebuildIndexIfNeeded()
        {
            PrototypeConfig config = PrototypeConfig.Active;
            if (config == _indexedConfig && _index != null)
            {
                return;
            }

            RebuildIndex();
        }

        /// <summary>
        /// Full rebuild: config asset plus a sweep for every other live owner of a tagged
        /// field. Costs one pass over the loaded objects, so it runs on open and on the
        /// Rescan button only. Rescan is what picks up owners that did not exist yet -
        /// pooled weapons, orbs, enemies spawned after the panel was last opened.
        /// </summary>
        private void RebuildIndex()
        {
            _indexedConfig = PrototypeConfig.Active;
            _index = ConfigFieldIndex.BuildAll(_indexedConfig);
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            EnsureStyles();
            _window = GUI.Window(0x00DD01, _window, DrawWindow, "DEATHS DEMOLITION - PROTOTYPE DEBUG (F1)");
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            _stylesReady = true;

            _headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };

            _groupStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.85f, 0.78f, 0.55f) }
            };

            _moduleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.95f, 0.95f, 0.95f) }
            };

            _originStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Italic,
                wordWrap = false,
                normal = { textColor = new Color(0.62f, 0.68f, 0.78f) }
            };

            // Invented values keep their own colour: they are the ones with no source at
            // all, so they must never blend into the merely-provisional ones.
            _inventedStyle = new GUIStyle(_originStyle)
            {
                normal = { textColor = new Color(0.92f, 0.62f, 0.35f) }
            };

            _rowLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12
            };
        }

        private void DrawWindow(int id)
        {
            DrawTabBar();
            GUILayout.Space(4f);

            _scroll = GUILayout.BeginScrollView(_scroll);

            if (_activeTab == DebugPanelTabs.Config)
            {
                DrawConfigTab();
            }
            else if (_activeTab == DebugPanelTabs.Candidates)
            {
                DrawCandidatesTab();
            }
            else
            {
                DrawRegisteredTab(_activeTab);
            }

            GUILayout.EndScrollView();

            GUILayout.Space(2f);
            DrawFooter();

            GUI.DragWindow(new Rect(0f, 0f, _window.width, 20f));
        }

        private void DrawTabBar()
        {
            _tabBuffer.Clear();
            IReadOnlyList<string> tabs = DebugPanelRegistry.Tabs;
            for (int i = 0; i < tabs.Count; i++)
            {
                _tabBuffer.Add(tabs[i]);
            }

            GUILayout.BeginHorizontal();
            for (int i = 0; i < _tabBuffer.Count; i++)
            {
                string tab = _tabBuffer[i];
                bool active = tab == _activeTab;
                GUI.color = active ? new Color(1f, 0.88f, 0.6f) : Color.white;
                if (GUILayout.Button(tab, GUILayout.MinWidth(70f)))
                {
                    _activeTab = tab;
                }
            }

            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }

        private void DrawFooter()
        {
            GUILayout.BeginHorizontal();
            PrototypeConfig config = PrototypeConfig.Active;
            string configName = config != null ? config.name : "<no config>";
            int revision = config != null ? config.Revision : 0;
            GUILayout.Label($"Config: {configName}  rev {revision}   |   Pools: {PoolService.Pools.Count}", _originStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(60f)))
            {
                SetVisible(false);
            }

            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ config tab

        private void DrawConfigTab()
        {
            RebuildIndexIfNeeded();

            if (_index == null)
            {
                GUILayout.Label("Index not built yet.", _headerStyle);
                return;
            }

            PrototypeConfig config = PrototypeConfig.Active;
            if (config == null)
            {
                GUILayout.Label("No PrototypeConfig loaded. Expected Resources/PrototypeConfig.", _headerStyle);
                GUILayout.Label("Values outside the config are still listed below.", _originStyle);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Filter", _rowLabelStyle, GUILayout.Width(40f));
            _search = GUILayout.TextField(_search ?? string.Empty, GUILayout.MinWidth(180f));
            if (GUILayout.Button("x", GUILayout.Width(24f)))
            {
                _search = string.Empty;
            }

            _showOriginTags = GUILayout.Toggle(_showOriginTags, "Origin tags", GUILayout.Width(90f));
            if (GUILayout.Button("Rescan", GUILayout.Width(70f)))
            {
                RebuildIndex();
            }

            GUILayout.EndHorizontal();

            DrawCoverageLine();

            GUILayout.Label(
                "Every value below is PROTOTYPE-ONLY. The line under a field is the planning decision it temporarily occupies; orange means the number was invented outright.",
                _originStyle);
            GUILayout.Label(
                "Rows outside CONFIG are written straight onto the live object: a system that read the value once at startup keeps its copy until it reads again.",
                _originStyle);
            GUILayout.Space(4f);

            string filter = string.IsNullOrEmpty(_search) ? null : _search.ToLowerInvariant();
            string currentModule = null;
            string currentGroup = null;
            bool changed = false;

            List<ConfigFieldBinding> bindings = _index.Bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                ConfigFieldBinding b = bindings[i];
                if (filter != null && b.Path.ToLowerInvariant().IndexOf(filter, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                if (b.Module != currentModule)
                {
                    currentModule = b.Module;
                    currentGroup = null;
                    GUILayout.Space(10f);
                    GUILayout.Label(currentModule.ToUpperInvariant(), _moduleStyle);
                }

                if (b.GroupPath != currentGroup)
                {
                    currentGroup = b.GroupPath;
                    GUILayout.Space(6f);
                    string suffix = b.OwnerInstanceCount > 1
                        ? "   (" + b.OwnerInstanceCount.ToString(CultureInfo.InvariantCulture)
                          + " live instances - editing the first)"
                        : string.Empty;
                    GUILayout.Label(currentGroup + suffix, _groupStyle);
                }

                changed |= DrawBindingRow(b);
            }

            DrawUnboundSection();

            if (changed && config != null)
            {
                config.NotifyChanged();
            }
        }

        /// <summary>
        /// What this surface covers and what it misses. A value nobody can see is the
        /// failure mode this panel exists to prevent, so the miss count is on screen.
        /// </summary>
        private void DrawCoverageLine()
        {
            int total = _index.Bindings.Count;
            int outside = _index.BoundOutsideConfig;
            int invented = 0;
            for (int i = 0; i < _index.Bindings.Count; i++)
            {
                ConfigFieldBinding b = _index.Bindings[i];
                if (b.Origin != null && b.Origin.Invented)
                {
                    invented++;
                }
            }

            GUILayout.Label(
                $"{total} values shown   |   {outside} outside PrototypeConfig   |   {invented} invented   |   "
                + $"{_index.UnboundCount} declared but unreachable   |   {_index.OwnersWithTaggedFields} live owners",
                _index.UnboundCount > 0 ? _inventedStyle : _originStyle);
        }

        /// <summary>Declared tagged fields the sweep could not bind. Named, never summarised away.</summary>
        private void DrawUnboundSection()
        {
            if (_index.UnboundCount == 0)
            {
                return;
            }

            GUILayout.Space(10f);
            _showUnbound = GUILayout.Toggle(_showUnbound,
                $"{_index.UnboundCount} declared PROTOTYPE-ONLY fields have no live owner right now (Rescan after spawning)");

            if (!_showUnbound)
            {
                return;
            }

            for (int i = 0; i < _index.Unbound.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(14f);
                GUILayout.Label(_index.Unbound[i], _originStyle);
                GUILayout.EndHorizontal();
            }
        }

        private bool DrawBindingRow(ConfigFieldBinding b)
        {
            bool changed = false;

            GUILayout.BeginHorizontal();
            GUILayout.Space(10f);
            GUILayout.Label(b.DisplayName, _rowLabelStyle, GUILayout.Width(210f));

            if (b.IsConstant)
            {
                // A const is already inlined at every call site; pretending it is editable
                // would be a lie the panel cannot make good on.
                GUILayout.Label(b.ValueAsString(), _rowLabelStyle, GUILayout.Width(120f));
                GUILayout.Label("const", _originStyle, GUILayout.Width(40f));
            }
            else if (b.IsBool)
            {
                bool current = (bool)b.GetValue();
                bool next = GUILayout.Toggle(current, current ? "on" : "off", GUILayout.Width(120f));
                if (next != current)
                {
                    b.SetValue(next);
                    changed = true;
                }
            }
            else if (b.IsEnum)
            {
                var current = (Enum)b.GetValue();
                if (GUILayout.Button(current.ToString(), GUILayout.Width(160f)))
                {
                    b.SetValue(NextEnumValue(b.FieldType, current));
                    changed = true;
                }
            }
            else if (b.IsNumeric)
            {
                changed |= DrawNumericField(b);
            }
            else if (b.IsString)
            {
                var current = (string)b.GetValue() ?? string.Empty;
                string next = GUILayout.TextField(current, GUILayout.Width(160f));
                if (next != current)
                {
                    b.SetValue(next);
                    changed = true;
                }
            }
            else
            {
                GUILayout.Label(b.ValueAsString(), _rowLabelStyle, GUILayout.Width(160f));
                GUILayout.Label("(Inspector only)", _originStyle, GUILayout.Width(90f));
            }

            bool flagged = CandidateDecisionLog.Contains(b.Path);
            GUI.color = flagged ? new Color(1f, 0.85f, 0.35f) : Color.white;
            if (GUILayout.Button(flagged ? "*" : "+", GUILayout.Width(24f)))
            {
                if (flagged)
                {
                    CandidateDecisionLog.Remove(b.Path);
                }
                else
                {
                    CandidateDecisionLog.Record(b);
                }
            }

            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (_showOriginTags && b.Origin != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20f);
                string tag = b.Origin.Invented
                    ? "PROTOTYPE-ONLY (invented) - " + b.Origin.EnglishLabel
                    : "PROTOTYPE-ONLY - " + b.Origin.EnglishLabel;
                GUILayout.Label(tag, b.Origin.Invented ? _inventedStyle : _originStyle);
                GUILayout.EndHorizontal();
            }

            return changed;
        }

        private bool DrawNumericField(ConfigFieldBinding b)
        {
            bool changed = false;
            bool isFloat = b.FieldType == typeof(float);

            string current = b.ValueAsString();
            if (b.EditBuffer == null)
            {
                b.EditBuffer = current;
            }

            // Something else (Inspector) changed the value while we were not editing.
            if (!GUI.GetNameOfFocusedControl().Equals(b.Path, StringComparison.Ordinal) && b.EditBuffer != current)
            {
                b.EditBuffer = current;
            }

            GUI.SetNextControlName(b.Path);
            string next = GUILayout.TextField(b.EditBuffer, GUILayout.Width(90f));
            b.EditBuffer = next;

            if (isFloat)
            {
                if (float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    if (b.HasRange)
                    {
                        parsed = Mathf.Clamp(parsed, b.RangeMin, b.RangeMax);
                    }

                    if (!Mathf.Approximately(parsed, (float)b.GetValue()))
                    {
                        b.SetValue(parsed);
                        changed = true;
                    }
                }
            }
            else
            {
                if (int.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    if (b.HasRange)
                    {
                        parsed = Mathf.Clamp(parsed, Mathf.RoundToInt(b.RangeMin), Mathf.RoundToInt(b.RangeMax));
                    }

                    if (parsed != (int)b.GetValue())
                    {
                        b.SetValue(parsed);
                        changed = true;
                    }
                }
            }

            if (b.HasRange && isFloat)
            {
                float value = (float)b.GetValue();
                float slid = GUILayout.HorizontalSlider(value, b.RangeMin, b.RangeMax, GUILayout.Width(120f));
                if (!Mathf.Approximately(slid, value))
                {
                    b.SetValue(slid);
                    b.EditBuffer = slid.ToString("0.####", CultureInfo.InvariantCulture);
                    changed = true;
                }
            }

            return changed;
        }

        private static object NextEnumValue(Type enumType, Enum current)
        {
            Array values = Enum.GetValues(enumType);
            int index = Array.IndexOf(values, current);
            index = (index + 1) % values.Length;
            return values.GetValue(index);
        }

        // -------------------------------------------------------------- registered tab

        private void DrawRegisteredTab(string tab)
        {
            IReadOnlyList<DebugPanelEntry> entries = DebugPanelRegistry.Entries;
            bool any = false;

            for (int i = 0; i < entries.Count; i++)
            {
                DebugPanelEntry entry = entries[i];
                if (entry.Tab != tab)
                {
                    continue;
                }

                if (entry.Visible != null && !entry.Visible())
                {
                    continue;
                }

                any = true;
                DrawEntry(entry);
            }

            if (!any)
            {
                GUILayout.Label("Nothing registered on this tab yet.", _originStyle);
            }
        }

        private void DrawEntry(DebugPanelEntry entry)
        {
            if (entry is DebugPanelButton button)
            {
                if (GUILayout.Button(button.Label, GUILayout.Height(24f)))
                {
                    button.OnPressed?.Invoke();
                }

                return;
            }

            if (entry is DebugPanelToggle toggle)
            {
                bool current = toggle.Get != null && toggle.Get();
                bool next = GUILayout.Toggle(current, entry.Label);
                if (next != current)
                {
                    toggle.Set?.Invoke(next);
                }

                return;
            }

            if (entry is DebugPanelSlider slider)
            {
                float value = slider.Get != null ? slider.Get() : 0f;
                GUILayout.BeginHorizontal();
                GUILayout.Label(entry.Label, _rowLabelStyle, GUILayout.Width(210f));
                float next = GUILayout.HorizontalSlider(value, slider.Min, slider.Max, GUILayout.Width(200f));
                if (slider.WholeNumbers)
                {
                    next = Mathf.Round(next);
                }

                GUILayout.Label(next.ToString("0.###", CultureInfo.InvariantCulture), _rowLabelStyle,
                    GUILayout.Width(70f));
                GUILayout.EndHorizontal();

                if (!Mathf.Approximately(next, value))
                {
                    slider.Set?.Invoke(next);
                }

                return;
            }

            if (entry is DebugPanelReadout readout)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(entry.Label, _rowLabelStyle, GUILayout.Width(210f));
                GUILayout.Label(readout.Text != null ? readout.Text() : string.Empty, _rowLabelStyle);
                GUILayout.EndHorizontal();
                return;
            }

            if (entry is DebugPanelSection section)
            {
                if (!string.IsNullOrEmpty(entry.Label))
                {
                    GUILayout.Label(entry.Label, _groupStyle);
                }

                section.Draw?.Invoke();
            }
        }

        // -------------------------------------------------------------- candidates tab

        private void DrawCandidatesTab()
        {
            GUILayout.Label("Candidate decision list", _headerStyle);
            GUILayout.Label(
                "Values flagged with '+' during play. Flagging changes nothing on its own: nothing reaches planning/ without an explicit instruction.",
                _originStyle);
            GUILayout.Space(6f);

            IReadOnlyList<CandidateDecision> entries = CandidateDecisionLog.Entries;
            if (entries.Count == 0)
            {
                GUILayout.Label("Empty.", _rowLabelStyle);
            }

            for (int i = 0; i < entries.Count; i++)
            {
                CandidateDecision e = entries[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(e.FieldPath, _rowLabelStyle, GUILayout.Width(280f));
                GUILayout.Label(e.Value, _rowLabelStyle, GUILayout.Width(90f));
                GUILayout.Label(e.Invented ? "invented - " + e.EnglishLabel : e.EnglishLabel,
                    e.Invented ? _inventedStyle : _originStyle);
                if (GUILayout.Button("-", GUILayout.Width(24f)))
                {
                    CandidateDecisionLog.Remove(e.FieldPath);
                    break;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Log as markdown", GUILayout.Width(160f)))
            {
                UnityEngine.Debug.Log(CandidateDecisionLog.ToMarkdown());
            }

            if (GUILayout.Button("Clear", GUILayout.Width(80f)))
            {
                CandidateDecisionLog.Clear();
            }

            GUILayout.EndHorizontal();
        }
    }
}
