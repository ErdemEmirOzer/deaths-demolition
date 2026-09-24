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
        Reroll = 4,
        /// <summary>WP-068 slice 8 (E11): the HUD's EMPTY prompt - the Gameplay Reload action (R / pad West), rebindable.</summary>
        Reload = 5
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

        /// <summary>Text inset on each side of a key cap (AD G1 MUST-2).</summary>
        public const float CapPadding = 12f;

        /// <summary>
        /// A disc / shape is square; a key cap grows with its label at the TextScale (AD G1 MUST-2: at Mac 1.2 × 1.25
        /// ENTER / ESC overflowed a cap sized for 1.0). The prompt also measures its live text (<see cref="UiPrompt"/>).
        /// </summary>
        public static float WidthOf(Glyph glyph, float size)
        {
            if (glyph.Round || HasShape(glyph))
            {
                return size;
            }

            return Mathf.Max(MinCapWidth, Mathf.Round(size * glyph.Width * Mathf.Clamp(UiTokens.TextScale, 1f, 1.25f)));
        }

        /// <summary>True when the glyph draws its V1 shape (the G1 atlas has it) instead of a cap with text.</summary>
        public static bool HasShape(Glyph glyph)
        {
            return glyph.Shape != null && UiArt.HasIcon(glyph.Shape);
        }

        /// <summary>
        /// Text is the control's name (tests, screen readers, and the cap label when there is no shape); Shape is the
        /// V1 input shape (G1 key: Pad.FaceSouth, Pad.Menu, Mouse.Left ...) drawn instead of a disc + letter - face
        /// buttons by their position in the diamond, so Xbox / PlayStation / Nintendo pads read alike (AP-012 v004 V1).
        /// </summary>
        public readonly struct Glyph
        {
            public readonly string Text;
            public readonly bool Round;
            public readonly float Width;
            public readonly string Shape;

            public Glyph(string text, bool round, float width, string shape = null)
            {
                Text = text;
                Round = round;
                Width = width;
                Shape = shape;
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
                case UiPromptAction.Submit: return pad ? new Glyph("A", true, 1f, "Pad.FaceSouth") : new Glyph("ENTER", false, 2.4f);
                case UiPromptAction.Cancel: return pad ? new Glyph("B", true, 1f, "Pad.FaceEast") : new Glyph("ESC", false, 1.8f);
                case UiPromptAction.Pause: return pad ? new Glyph("MENU", false, 2.2f, "Pad.Menu") : new Glyph("ESC", false, 1.8f);
                case UiPromptAction.Skip: return pad ? new Glyph("X", true, 1f, "Pad.FaceWest") : new Glyph("SPACE", false, 2.6f);
                case UiPromptAction.Reroll: return pad ? new Glyph("Y", true, 1f, "Pad.FaceNorth") : new Glyph("R", false, 1.2f);
                case UiPromptAction.Reload: return pad ? new Glyph("X", true, 1f, "Pad.FaceWest") : new Glyph("R", false, 1.2f);
                default: return new Glyph("?", true, 1f);
            }
        }

        /// <summary>The Gameplay action a prompt action is bound to, or null when the prompt is a fixed UI key (Submit / Cancel / Skip / Reroll).</summary>
        public static string GameplayActionOf(UiPromptAction action)
        {
            switch (action)
            {
                case UiPromptAction.Pause: return PrototypeInputNames.ActionPause;
                case UiPromptAction.Reload: return PrototypeInputNames.ActionReload;
                default: return null;
            }
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
            return new Glyph(label, round, Mathf.Max(1.2f, 0.6f + 0.4f * label.Length), ShapeOf(label, scheme));
        }

        /// <summary>The V1 shape for a short control label, or null (keys stay text on a cap).</summary>
        public static string ShapeOf(string label, InputSchemeKind scheme)
        {
            if (scheme == InputSchemeKind.Gamepad)
            {
                switch (label)
                {
                    case "A": return "Pad.FaceSouth";
                    case "B": return "Pad.FaceEast";
                    case "X": return "Pad.FaceWest";
                    case "Y": return "Pad.FaceNorth";
                    case "MENU": return "Pad.Menu";
                    case "VIEW": return "Pad.View";
                    case "LB": return "Pad.LB";
                    case "RB": return "Pad.RB";
                    case "LT": return "Pad.LT";
                    case "RT": return "Pad.RT";
                    case "LS": return "Pad.LSClick";
                    case "RS": return "Pad.RSClick";
                    case "D-UP": return "Pad.DUp";
                    case "D-DOWN": return "Pad.DDown";
                    case "D-LEFT": return "Pad.DLeft";
                    case "D-RIGHT": return "Pad.DRight";
                    default: return null;
                }
            }

            switch (label)
            {
                case "LMB": return "Mouse.Left";
                case "RMB": return "Mouse.Right";
                case "MMB": return "Mouse.Middle";
                default: return null;
            }
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

            // V1: a pad button / mouse button draws its shape (cream on a primary button, the ink colour on a dark cap).
            Image shape = UiFactory.NewImage("Shape", rect, paper == UiColor.TextPrimary ? paper : ink);
            UiFactory.Fill((RectTransform)shape.transform);
            shape.preserveAspect = true;
            shape.enabled = false;

            // V1 key cap: the authored cap is an outline (rim + bevel), so it rides over the filled pill in the text's
            // colour - cream text + cream rim on a dark cap, dark text + dark rim on the cream cap of a primary button.
            Image rim = UiFactory.NewImage("Rim", rect, ink);
            UiFactory.Fill((RectTransform)rim.transform);
            rim.enabled = false;

            TMP_Text text = UiFactory.Label("Text", rect, string.Empty, UiType.Caption, ink, TextAnchor.MiddleCenter);
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.fontSize = UiTokens.FontSize(UiType.Caption); // 14 px (AD v005 MUST-4)
            text.fontStyle = FontStyles.Bold;
            text.characterSpacing = 2f;
            UiFactory.Fill((RectTransform)text.transform);

            UiPrompt prompt = go.AddComponent<UiPrompt>();
            prompt.Configure(action, back, shape, rim, text, size);
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
        private Image _shape;
        private Image _rim;
        private float _widestCap; // the widest cap this prompt has drawn: the label never shifts back on a scheme flip
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

            // The widest the glyph can be on either device, or its measured cap now (AD G1 MUST-2) - whichever is wider.
            float reserve = Mathf.Max(UiInputGlyphs.ReservedWidth(_action, _size), _widestCap) + (int)UiSpace.M;
            Vector2 max = _label.offsetMax;
            float target = _labelBaseOffsetMax - reserve;
            if (!Mathf.Approximately(max.x, target))
            {
                _label.offsetMax = new Vector2(target, max.y);
            }
        }

        /// <summary>The text currently drawn ("A", "ENTER"...). Tests read it.</summary>
        public string CurrentText => _text != null ? _text.text : string.Empty;

        /// <summary>The V1 shape currently drawn ("Pad.FaceSouth"...), or null when the prompt is a cap with text. Tests read it.</summary>
        public string CurrentShape => _shape != null && _shape.enabled && _shape.sprite != null ? _shape.sprite.name : null;

        internal void Configure(UiPromptAction action, Image back, Image shape, Image rim, TMP_Text text, float size)
        {
            _action = action;
            _back = back;
            _shape = shape;
            _rim = rim;
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

        private bool _applied;

        /// <summary>
        /// WP-068 8c · E23: a device flip or a rebind swaps the glyph - the new one fades in over Fast (Decor: at once
        /// under reduced motion), so the swap reads as one control changing its face rather than a flicker.
        /// </summary>
        private void Apply(InputSchemeKind scheme)
        {
            string before = _applied ? Signature() : null;
            ApplyGlyph(scheme);
            if (_applied && isActiveAndEnabled && before != Signature())
            {
                FadeIn(_shape);
                FadeIn(_text);
                FadeIn(_back);
                FadeIn(_rim);
            }

            _applied = true;
        }

        private string Signature()
        {
            return _shape != null && _shape.enabled && _shape.sprite != null ? _shape.sprite.name : _text.text;
        }

        private static void FadeIn(Graphic graphic)
        {
            if (graphic == null || !graphic.enabled)
            {
                return;
            }

            graphic.canvasRenderer.SetAlpha(0f);
            UiMotion.TintAlpha(graphic, 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
        }

        private void ApplyGlyph(InputSchemeKind scheme)
        {
            UiInputGlyphs.Glyph glyph = UiInputGlyphs.For(_action, scheme);
            var rect = (RectTransform)transform;
            if (_text.text != glyph.Text)
            {
                _text.text = glyph.Text;   // kept as the control's name (tests, screen reader) even when the shape draws
            }

            // V1 shape: the pad / mouse button itself, no cap behind it.
            Sprite shape = glyph.Shape != null ? UiArt.Icon(glyph.Shape) : null;
            bool drawShape = shape != null && _shape != null;
            float width = UiInputGlyphs.WidthOf(glyph, _size);
            if (!drawShape && !glyph.Round)
            {
                // AD G1 MUST-2: the cap encloses its live text at the TextScale (measured on a scheme flip, never per frame).
                width = Mathf.Max(width, Mathf.Ceil(_text.GetPreferredValues(glyph.Text).x) + 2f * UiInputGlyphs.CapPadding);
            }

            rect.sizeDelta = new Vector2(width, _size);
            _widestCap = Mathf.Max(_widestCap, width);
            ApplyLabelReserve();
            if (_shape != null)
            {
                if (drawShape && _shape.sprite != shape)
                {
                    UiArt.SetGlyph(_shape, shape);
                }

                _shape.enabled = drawShape;
            }

            _text.enabled = !drawShape;
            Transform edge = transform.Find("Edge");
            Image edgeImage = edge != null ? edge.GetComponent<Image>() : null;
            _back.enabled = !drawShape;
            if (edgeImage != null)
            {
                edgeImage.enabled = !drawShape;
            }

            // A key: the kit's filled pill in the paper colour (the text's contrast is measured on it) with the V1 cap
            // outline over it (9-sliced, authored 2× at 128 px tall → drawn at the prompt's height). A pad button without
            // its shape keeps the disc.
            Sprite cap = drawShape || glyph.Round ? null : UiArt.KeyCap(width / _size > 1.25f);
            if (_rim != null)
            {
                _rim.enabled = cap != null;
                if (cap != null)
                {
                    _rim.sprite = cap;
                    _rim.type = Image.Type.Sliced;
                    _rim.fillCenter = false;
                    _rim.pixelsPerUnitMultiplier = cap.rect.height / Mathf.Max(1f, _size);
                }
            }

            if (drawShape)
            {
                return;
            }

            _back.sprite = glyph.Round ? UiSprites.Disc : UiSprites.Chip;
            _back.type = glyph.Round ? Image.Type.Simple : Image.Type.Sliced;
            if (edgeImage != null)
            {
                edgeImage.sprite = _back.sprite;
                edgeImage.type = _back.type;
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
