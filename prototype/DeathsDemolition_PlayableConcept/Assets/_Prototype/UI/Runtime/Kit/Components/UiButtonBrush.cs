using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-098A (KIT-API §3, §4 "Brush düğme"; KIT-KILAVUZU §4.1 C10 / C11 / C17) · the frameless <see cref="UiButtonVariant.Brush"/>
    /// half of UiButton. The factory builds no halo and no ring and skips the sheen and the AP-012 frame; the fill, border
    /// and hover wash exist (the state code keeps one shape) but stay disabled - no overdraw. What the state matrix becomes:
    ///   rest / hover   the word (MenuBody) and glyph in TextPrimary - TextSecondary for a choice row's other items;
    ///   focused        the cream brackets (<see cref="UiFocusBrackets"/>, opaque on frame 1) - never a ring, never an underline;
    ///   pressed        <see cref="UiPresets.PressDepth"/> 0.98 (pointer: while held; Submit: <see cref="PressPulse"/>, 80 ms, then the release);
    ///   lifted         WP-098A (HAREKET-MENU M8): the word and glyph rise 2 px (<see cref="UiPresets.HoverLift"/>) and the ink wave
    ///                  ripples behind (<see cref="UiInkWave"/>) - neither under Reduced Motion;
    ///   disabled       TextSecondary word and glyph + a 1 px hairline in the line tone under them ("DisabledRule", built the
    ///                  first time the control is disabled); the reason belongs in the tooltip.
    ///
    /// USAGE: through <see cref="UiBrushKit.Button"/> / <see cref="UiBrushKit.IconButton"/> / <see cref="UiBrushKit.Segments"/>;
    /// any UiFactory.Button(…, UiButtonVariant.Brush) behaves the same.
    /// DON'T: give a Brush control a fill, a frame or a Danger colour (a destructive action is worded, KILAVUZ §1), or draw a
    /// focus underline - the brackets are focus, the C8 is a choice.
    /// ACCESSIBILITY: the word stays ≥ 4.5:1 on the window ink in every state (disabled TextSecondary 5.10:1); disabled is
    /// also a shape (the hairline), not only a tone; the press depth is Decor (none under Reduced Motion).
    /// </summary>
    public sealed partial class UiButton
    {
        private Image _disabledRule;
        private Image _brushGlyph;
        private bool _brushPressed;

        /// <summary>True when the control draws its own v3 focus ring; Brush leaves focus to <see cref="UiFocusBrackets"/>.</summary>
        public bool DrawsFocusRing
        {
            get { return _variant != UiButtonVariant.Brush && _ring != null; }
        }

        /// <summary>WP-098A · the disabled hairline is on screen (tests).</summary>
        public bool DisabledRuleVisible
        {
            get { return _disabledRule != null && _disabledRule.gameObject.activeSelf; }
        }

        /// <summary>WP-098A · a C11's leading glyph: tinted with the word (TextPrimary / TextSecondary) by the state code.</summary>
        internal Image BrushGlyph
        {
            get { return _brushGlyph; }
            set
            {
                _brushGlyph = value;
                if (_configured)
                {
                    Refresh(true);
                }
            }
        }

        private void ConfigureBrush()
        {
            if (_fill != null) _fill.enabled = false;
            if (_border != null) _border.enabled = false;
            if (_wash != null) _wash.enabled = false;
        }

        /// <summary>Called by Refresh for Brush: the press depth, the disabled hairline, the glyph's tone. Event-time only.</summary>
        private void RefreshBrush(bool enabledControl, bool pressed)
        {
            if (pressed != _brushPressed)
            {
                _brushPressed = pressed;
                var rect = (RectTransform)transform;
                if (pressed && !UiMotion.ReducedMotion)
                {
                    UiPresets.PressDepth(rect);
                }
                else if (!pressed)
                {
                    UiPresets.PressRelease(rect);
                }
            }

            if (!enabledControl && _disabledRule == null)
            {
                // The line tone, 1 px, across the control inset by a step: under the word (C11) or the glyph's hit (C10).
                _disabledRule = UiFactory.NewImage("DisabledRule", transform, UiColor.TextMuted, UiSprites.Solid);
                _disabledRule.color = UiBrushTokens.LineColour();
                var rule = (RectTransform)_disabledRule.transform;
                rule.anchorMin = new Vector2(0f, 0f);
                rule.anchorMax = new Vector2(1f, 0f);
                rule.pivot = new Vector2(0.5f, 0f);
                rule.offsetMin = new Vector2((int)UiSpace.L, (int)UiSpace.S);
                rule.offsetMax = new Vector2(-(int)UiSpace.L, (int)UiSpace.S + UiTokens.StrokeHairline);
            }

            if (_disabledRule != null && _disabledRule.gameObject.activeSelf == enabledControl)
            {
                _disabledRule.gameObject.SetActive(!enabledControl);
            }

            if (_brushGlyph != null && _brushGlyph.color != _labelTarget)
            {
                _brushGlyph.color = _labelTarget;   // one rebuild per state change, never per frame
            }
        }
    }
}
