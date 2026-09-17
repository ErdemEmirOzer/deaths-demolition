using System;
using DD.Prototype.Core.InputAbstraction;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
    /// from the Input System's device timestamps (the WP-024 gameplay source only runs in a
    /// run; menus need the same answer). No allocation per frame; <see cref="Changed"/>
    /// fires only on a flip. Menus and the run agree: a run in progress also raises the
    /// WP-024 <c>SchemeChanged</c>, and the two always land on the same value because both
    /// read the same devices.
    /// </summary>
    public static class UiInputScheme
    {
        private static InputSchemeKind s_current = InputSchemeKind.KeyboardMouse;
        private static double s_lastGamepad = -1d;
        private static double s_lastKeyboard = -1d;
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

        /// <summary>Called by the prompt driver once per frame.</summary>
        internal static void Poll()
        {
            if (s_forced)
            {
                return;
            }

            Gamepad pad = Gamepad.current;
            double padTime = pad != null ? pad.lastUpdateTime : -1d;
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            double keyTime = keyboard != null ? keyboard.lastUpdateTime : -1d;
            if (mouse != null && mouse.lastUpdateTime > keyTime)
            {
                keyTime = mouse.lastUpdateTime;
            }

            // Mouse movement updates every frame on a desktop; only a NEW pad update after
            // the last keyboard/mouse update flips to Gamepad, and vice versa. The first
            // frame with no history stays on KeyboardMouse (the desktop default).
            if (padTime > s_lastGamepad && padTime > keyTime && s_current != InputSchemeKind.Gamepad)
            {
                Set(InputSchemeKind.Gamepad);
            }
            else if (keyTime > s_lastKeyboard && keyTime > padTime && s_current != InputSchemeKind.KeyboardMouse)
            {
                Set(InputSchemeKind.KeyboardMouse);
            }

            s_lastGamepad = padTime;
            s_lastKeyboard = keyTime;
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
            s_lastGamepad = -1d;
            s_lastKeyboard = -1d;
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
            return Mathf.Max(WidthOf(For(action, InputSchemeKind.Gamepad), size), WidthOf(For(action, InputSchemeKind.KeyboardMouse), size));
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

        /// <summary>The glyph for an action on a device. Gamepad labels follow the Xbox layout (the WP-024 default); keys are the WP-024 defaults.</summary>
        public static Glyph For(UiPromptAction action, InputSchemeKind scheme)
        {
            bool pad = scheme == InputSchemeKind.Gamepad;
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

        private void OnEnable()
        {
            UiInputScheme.Changed += Apply;
            if (_text != null)
            {
                Apply(UiInputScheme.Current);
            }
        }

        private void OnDisable()
        {
            UiInputScheme.Changed -= Apply;
        }

        private void Apply(InputSchemeKind scheme)
        {
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
