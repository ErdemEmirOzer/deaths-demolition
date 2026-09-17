using System;
using System.Collections.Generic;
using DD.Prototype.Core.InputAbstraction;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// The UI action a prompt explains. One vocabulary for every screen: a prompt never
    /// names a key or a button directly, it names the ACTION, and <see cref="UiInputGlyphs"/>
    /// draws whatever the active device calls it.
    /// </summary>
    public enum UiPromptAction
    {
        Submit = 0,
        Cancel = 1,
        Pause = 2,
        Skip = 3,
        Reroll = 4
    }

    /// <summary>
    /// WP-022F · which device the player touched last, for prompts. Polled once per frame
    /// from the WP-024 input abstraction (<see cref="InputSourceRegistry"/>); no Input
    /// System type in the UI (ADR-024-2). No allocation per frame; <see cref="Changed"/>
    /// fires only on a flip.
    /// </summary>
    public static class UiInputScheme
    {
        private static InputSchemeKind s_current = InputSchemeKind.KeyboardMouse;
        private static bool s_forced;

        public static InputSchemeKind Current => s_current;

        public static event Action<InputSchemeKind> Changed;

        /// <summary>Test seam: pin the scheme (tests, captures); <c>Release</c> returns to polling.</summary>
        public static void Force(InputSchemeKind scheme)
        {
            s_forced = true;
            Set(scheme);
        }

        public static void Release()
        {
            s_forced = false;
        }

        /// <summary>
        /// Called by the prompt driver once per frame. Reads the WP-024 input abstraction
        /// only (ADR-024-2: the Input System package is touched in Assets/_Prototype/Input
        /// alone - WP-059 parity contract); the source answers the last device that spoke,
        /// in menus as well as in a run. S-02's device-clock surface replaces this read
        /// when it lands; the prompts themselves do not change.
        /// </summary>
        internal static void Poll()
        {
            if (s_forced)
            {
                return;
            }

            IInputSource source = InputSourceRegistry.Current;
            if (source == null)
            {
                return;
            }

            InputSchemeKind scheme = source.ActiveScheme();
            if (scheme != s_current)
            {
                Set(scheme);
            }
        }

        private static void Set(InputSchemeKind scheme)
        {
            if (scheme == s_current)
            {
                return;
            }

            s_current = scheme;
            Action<InputSchemeKind> handlers = Changed;
            if (handlers != null)
            {
                try
                {
                    handlers(scheme);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_current = InputSchemeKind.KeyboardMouse;
            s_forced = false;
            Changed = null;
        }
    }

    /// <summary>
    /// WP-022F · button / key glyphs drawn from the kit's own sprites (AP-012 pattern, no
    /// credits): a gamepad face button is a disc with its letter, a shoulder / menu button
    /// a pill, a key a key-cap with its name. One place decides what an action is called on
    /// each device, so every prompt on every screen says the same thing.
    /// </summary>
    public static class UiInputGlyphs
    {
        public const float DefaultSize = 26f;

        /// <summary>Minimum key-cap width (AD v005 MUST-4); discs stay DefaultSize.</summary>
        public const float MinCapWidth = 44f;

        /// <summary>The widest glyph this action draws on any device: what a button reserves so its label never shifts on a scheme flip.</summary>
        public static float ReservedWidth(UiPromptAction action, float size = DefaultSize)
        {
            // Both schemes, and - for a rebindable action - both the rebound name and the
            // default vocabulary, so the label never moves whichever the player picks.
            float reserve = Mathf.Max(WidthOf(For(action, InputSchemeKind.Gamepad), size), WidthOf(For(action, InputSchemeKind.KeyboardMouse), size));
            return Mathf.Max(reserve, MinCapWidth);
        }

        public static float WidthOf(Glyph glyph, float size)
        {
            return glyph.Round ? size : Mathf.Max(MinCapWidth, Mathf.Round(size * glyph.Width));
        }

        public readonly struct Glyph
        {
            public readonly string Text;
            public readonly bool Round;
            public readonly float Width;

            public Glyph(string text, bool round, float width)
            {
                Text = text;
                Round = round;
                Width = width;
            }
        }

        /// <summary>
        /// The glyph for an action on a device. Gamepad labels follow the Xbox layout (the
        /// WP-024 default); keys are the WP-024 defaults. WP-059: a prompt whose action is a
        /// rebindable Gameplay slot (Pause) shows the REBOUND control's name from the WP-024
        /// remap service when the player changed it (<see cref="Rebound"/>).
        /// </summary>
        public static Glyph For(UiPromptAction action, InputSchemeKind scheme)
        {
            bool pad = scheme == InputSchemeKind.Gamepad;
            if (Rebound(action, scheme, out Glyph rebound))
            {
                return rebound;
            }

            switch (action)
            {
                case UiPromptAction.Submit: return pad ? new Glyph("A", true, 1f) : new Glyph("ENTER", false, 2.4f);
                case UiPromptAction.Cancel: return pad ? new Glyph("B", true, 1f) : new Glyph("ESC", false, 1.8f);
                case UiPromptAction.Pause: return pad ? new Glyph("MENU", false, 2.2f) : new Glyph("ESC", false, 1.8f);
                case UiPromptAction.Skip: return pad ? new Glyph("X", true, 1f) : new Glyph("SPACE", false, 2.6f);
                case UiPromptAction.Reroll: return pad ? new Glyph("Y", true, 1f) : new Glyph("R", false, 1.2f);
                default: return new Glyph("?", true, 1f);
            }
        }

        /// <summary>The Gameplay action a prompt action is bound to, or null when the prompt is a fixed UI key (Submit / Cancel / Skip / Reroll).</summary>
        public static string GameplayActionOf(UiPromptAction action)
        {
            return action == UiPromptAction.Pause ? PrototypeInputNames.ActionPause : null;
        }

        /// <summary>
        /// WP-059: true (and the glyph) when the player rebound the prompt's Gameplay action on
        /// <paramref name="scheme"/>; false leaves the default label. Reads the live
        /// <see cref="InputRemapRegistry"/> service; no service (menus before the installer,
        /// tests) means no override. Never allocates when nothing is overridden.
        /// </summary>
        public static bool Rebound(UiPromptAction action, InputSchemeKind scheme, out Glyph glyph)
        {
            glyph = default;
            string gameplayAction = GameplayActionOf(action);
            IInputRemapService remap = InputRemapRegistry.Current;
            if (gameplayAction == null || remap == null)
            {
                return false;
            }

            IReadOnlyList<InputRemapEntry> entries = remap.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                InputRemapEntry e = entries[i];
                if (e.Scheme != scheme || e.Action != gameplayAction || !string.IsNullOrEmpty(e.CompositePart) || !e.IsOverridden)
                {
                    continue;
                }

                glyph = FromControlName(e.DisplayName, scheme);
                return true;
            }

            return false;
        }

        /// <summary>
        /// The glyph a control's human-readable name draws: Xbox face-button letters as discs,
        /// shoulders / triggers / sticks / menu buttons as short pills, keys as their upper-case
        /// name (Escape → ESC, Space → SPACE). Width grows with the label so it never clips.
        /// </summary>
        public static Glyph FromControlName(string displayName, InputSchemeKind scheme)
        {
            string label = ShortLabel(displayName, scheme, out bool round);
            return new Glyph(label, round, Mathf.Max(1.2f, 0.6f + 0.4f * label.Length));
        }

        private static string ShortLabel(string displayName, InputSchemeKind scheme, out bool round)
        {
            round = false;
            if (string.IsNullOrEmpty(displayName))
            {
                return "?";
            }

            if (scheme == InputSchemeKind.Gamepad)
            {
                switch (displayName)
                {
                    case "Button South": case "A": round = true; return "A";
                    case "Button East": case "B": round = true; return "B";
                    case "Button West": case "X": round = true; return "X";
                    case "Button North": case "Y": round = true; return "Y";
                    case "Start": return "MENU";
                    case "Select": return "VIEW";
                    case "Left Shoulder": return "LB";
                    case "Right Shoulder": return "RB";
                    case "Left Trigger": return "LT";
                    case "Right Trigger": return "RT";
                    case "Left Stick Press": return "LS";
                    case "Right Stick Press": return "RS";
                }

                if (displayName.StartsWith("D-Pad ", StringComparison.Ordinal))
                {
                    return "D-" + displayName.Substring(6).ToUpperInvariant();
                }
            }
            else
            {
                switch (displayName)
                {
                    case "Escape": return "ESC";
                    case "Space": return "SPACE";
                    case "Enter": case "Return": return "ENTER";
                    case "Backspace": return "BKSP";
                    case "Left Shift": case "Right Shift": return "SHIFT";
                    case "Left Ctrl": case "Right Ctrl": case "Left Control": case "Right Control": return "CTRL";
                    case "Left Alt": case "Right Alt": return "ALT";
                    case "Left Button": return "LMB";
                    case "Right Button": return "RMB";
                    case "Middle Button": return "MMB";
                }
            }

            string upper = displayName.ToUpperInvariant();
            return upper.Length <= 8 ? upper : upper.Substring(0, 8);
        }

        /// <summary>
        /// Builds a glyph (background + text) under <paramref name="parent"/>, sized
        /// <paramref name="size"/> tall. The returned <see cref="UiPrompt"/> keeps it in step
        /// with the active device.
        /// </summary>
        public static UiPrompt Build(string name, Transform parent, UiPromptAction action, UiColor ink, UiColor paper, float size = DefaultSize)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(size, size);

            Image back = UiFactory.NewImage("Back", rect, paper, UiSprites.Disc);
            UiFactory.Fill((RectTransform)back.transform);

            // AD v005 MUST-4: 1 px hairline on the inset (dark) cap so it separates from
            // the button fill; the cream cap on a primary button needs none.
            if (paper != UiColor.TextPrimary)
            {
                Image edge = UiFactory.NewImage("Edge", rect, UiColor.BorderHairline, UiSprites.Disc);
                RectTransform edgeRect = UiFactory.Fill((RectTransform)edge.transform);
                edgeRect.offsetMin = new Vector2(-1f, -1f);
                edgeRect.offsetMax = new Vector2(1f, 1f);
                edge.transform.SetAsFirstSibling();
            }

            TMP_Text text = UiFactory.Label("Text", rect, string.Empty, UiType.Caption, ink, TextAnchor.MiddleCenter);
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.fontSize = UiTokens.FontSize(UiType.Caption); // 14 px (AD v005 MUST-4)
            text.fontStyle = FontStyles.Bold;
            text.characterSpacing = 2f;
            UiFactory.Fill((RectTransform)text.transform);

            UiPrompt prompt = go.AddComponent<UiPrompt>();
            prompt.Configure(action, back, text, size);
            return prompt;
        }
    }

    /// <summary>
    /// A glyph that redraws itself when the active device flips. Zero work per frame: the
    /// driver polls the devices once, the prompt only reacts to <see cref="UiInputScheme.Changed"/>.
    /// </summary>
    public sealed class UiPrompt : MonoBehaviour
    {
        private UiPromptAction _action;
        private Image _back;
        private TMP_Text _text;
        private float _size;

        public UiPromptAction Action => _action;

        // WP-022J (S-02 devir): the button label this prompt sits in and its untouched
        // right offset, so a rebind to a wider name ("Left Trigger") re-insets the label
        // instead of overlapping the cap.
        private RectTransform _label;
        private float _labelBaseOffsetMax;
        private bool _hasLabel;

        internal void AttachLabel(RectTransform label, float baseOffsetMax)
        {
            _label = label;
            _labelBaseOffsetMax = baseOffsetMax;
            _hasLabel = label != null;
            ApplyLabelReserve();
        }

        private void ApplyLabelReserve()
        {
            if (!_hasLabel || _label == null)
            {
                return;
            }

            float reserve = UiInputGlyphs.ReservedWidth(_action, _size) + (int)UiSpace.M;
            Vector2 max = _label.offsetMax;
            float target = _labelBaseOffsetMax - reserve;
            if (!Mathf.Approximately(max.x, target))
            {
                _label.offsetMax = new Vector2(target, max.y);
            }
        }

        /// <summary>The text currently drawn ("A", "ENTER"...). Tests read it.</summary>
        public string CurrentText => _text != null ? _text.text : string.Empty;

        internal void Configure(UiPromptAction action, Image back, TMP_Text text, float size)
        {
            _action = action;
            _back = back;
            _text = text;
            _size = size;
            UiPromptDriver.Ensure();
            Apply(UiInputScheme.Current);
        }

        private IInputRemapService _remap;

        private void OnEnable()
        {
            UiInputScheme.Changed += Apply;
            InputRemapRegistry.CurrentChanged += OnRemapChanged;
            Watch(InputRemapRegistry.Current);
            if (_text != null)
            {
                Apply(UiInputScheme.Current);
            }
        }

        private void OnDisable()
        {
            UiInputScheme.Changed -= Apply;
            InputRemapRegistry.CurrentChanged -= OnRemapChanged;
            Watch(null);
        }

        // WP-059: a rebind (or reset) redraws the prompt at once — the pause screen's "same key closes it" stays true.
        private void Watch(IInputRemapService remap)
        {
            if (ReferenceEquals(_remap, remap))
            {
                return;
            }

            if (_remap != null)
            {
                _remap.BindingsChanged -= OnBindingsChanged;
            }

            _remap = remap;
            if (_remap != null)
            {
                _remap.BindingsChanged += OnBindingsChanged;
            }
        }

        private void OnRemapChanged(IInputRemapService remap)
        {
            Watch(remap);
            OnBindingsChanged();
        }

        private void OnBindingsChanged()
        {
            if (_text != null)
            {
                Apply(UiInputScheme.Current);
            }
        }

        /// <summary>Redraw now (tests / screens that rebuilt their entries).</summary>
        public void Refresh()
        {
            OnBindingsChanged();
        }

        private void Apply(InputSchemeKind scheme)
        {
            ApplyLabelReserve();
            UiInputGlyphs.Glyph glyph = UiInputGlyphs.For(_action, scheme);
            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(UiInputGlyphs.WidthOf(glyph, _size), _size);
            _back.sprite = glyph.Round ? UiSprites.Disc : UiSprites.Chip;
            _back.type = glyph.Round ? Image.Type.Simple : Image.Type.Sliced;
            Transform edge = transform.Find("Edge");
            if (edge != null)
            {
                var edgeImage = edge.GetComponent<Image>();
                edgeImage.sprite = _back.sprite;
                edgeImage.type = _back.type;
            }
            if (_text.text != glyph.Text)
            {
                _text.text = glyph.Text;
            }
        }
    }

    /// <summary>One scene-persistent poller for <see cref="UiInputScheme"/>; created on first prompt.</summary>
    public sealed class UiPromptDriver : MonoBehaviour
    {
        private static UiPromptDriver s_instance;

        internal static void Ensure()
        {
            if (s_instance != null)
            {
                return;
            }

            var go = new GameObject("UiPromptDriver");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<UiPromptDriver>();
        }

        private void Update()
        {
            UiInputScheme.Poll();
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }
    }
}
