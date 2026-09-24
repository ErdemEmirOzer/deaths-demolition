using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 5 · component #13 UiCard (direction §4) - the FramedCard façade with the
    /// layer contract: shadow → material (surface) → visual → AP-012 frame → title → text →
    /// inset shelf. Callers fill <see cref="Content"/>; the card owns the shadow and the lift.
    ///
    /// Elevation (§1: bevel + shadow): Elev1/Elev2 draw the bevel - 1 px SheenRim along the top
    /// inside the frame, 2 px ink along the bottom - which is the cue that survives a dark page
    /// (a soft shadow on Sumi800 cannot read at any alpha; AD 5b MUST-5), plus the soft shadow
    /// (Elev1 8 px 0.35, Elev2 24 px 0.55) for plates, washi and the frozen-frame blur. A
    /// selectable card rests at Elev1 and lifts to Elev2 on hover / focus: y +4 px, fill
    /// SurfacePanel → SurfaceRaised and the shadows crossfade (Fast), together with the
    /// travelling focus brackets. A LOCKED card never lifts. The current choice is not a
    /// lift: Elev1 + raised surface + cream outline + chip (the screen's UiButton Card state).
    ///
    /// USAGE: every framed panel that holds a unit of content (a weapon, a shop offer, a
    /// profile block, a medal row). DON'T: nest a card in a card, lift a card that cannot be
    /// chosen, or grow the shadow permanently to show selection.
    /// ACCESSIBILITY: the shadow is decoration (the frame carries the edge in both palettes);
    /// the lift is Decor - Reduced Motion shows the lifted state without motion.
    /// </summary>
    public sealed class UiCard : MonoBehaviour
    {
        public const float LiftPx = 4f;

        /// <summary>The bevel sits this far in from the card edge: inside the AP-012 card frame.</summary>
        public const float FrameInset = 8f;

        private RectTransform _body;
        private RectTransform _content;
        private Image _restShadow;
        private Image _liftShadow;
        private Image _fill;
        private UiColor _surface;
        private UiElevation _rest;
        private bool _lifted;
        private bool _locked;

        public RectTransform Body
        {
            get { return _body; }
        }

        /// <summary>Padded content rect (title, text, shelf go here).</summary>
        public RectTransform Content
        {
            get { return _content; }
        }

        public bool Lifted
        {
            get { return _lifted; }
        }

        public bool Locked
        {
            get { return _locked; }
        }

        /// <summary>The fill colour now (tests: the lift steps it to SurfaceRaised).</summary>
        public Color FillColour
        {
            get { return _fill != null ? _fill.canvasRenderer.GetColor() : Color.clear; }
        }

        public static UiCard Create(string name, Transform parent, Vector2 size, UiElevation elevation = UiElevation.Elev1,
            UiColor surface = UiColor.SurfacePanel, UiSpace padding = UiSpace.L)
        {
            RectTransform root = UiFactory.NewRect(name, parent);
            root.sizeDelta = size;
            var card = root.gameObject.AddComponent<UiCard>();
            card._rest = elevation;

            card._restShadow = Shadow(root, elevation == UiElevation.Elev2 ? UiElevation.Elev2 : UiElevation.Elev1);
            card._liftShadow = Shadow(root, UiElevation.Elev2);
            card._restShadow.canvasRenderer.SetAlpha(elevation == UiElevation.Elev0 ? 0f : 1f);
            card._liftShadow.canvasRenderer.SetAlpha(0f);

            card._body = UiFactory.FramedCard("Body", root, UiColor.BorderHairline, surface, UiFrame.Card);
            UiFactory.Fill(card._body);
            card._surface = surface;

            // The fill's colour lives on the CanvasRenderer (Tint channel) so the lift can step it.
            Transform fillTransform = card._body.Find("Fill");
            card._fill = fillTransform != null ? fillTransform.GetComponent<Image>() : null;
            if (card._fill != null)
            {
                card._fill.color = Color.white;
                card._fill.canvasRenderer.SetColor(UiTokens.Get(surface));
            }

            if (elevation != UiElevation.Elev0)
            {
                Bevel(card._body, "Sheen", UiColor.SheenRim, 1f, true);
                Bevel(card._body, "Base", UiColor.ShadowKey, 2f, false);
            }

            card._content = UiFactory.CardContent(card._body, padding);
            return card;
        }

        /// <summary>One bevel line inside the frame: <paramref name="top"/> = the sheen along the top, else the ink along the bottom.</summary>
        private static void Bevel(RectTransform body, string name, UiColor colour, float height, bool top)
        {
            Image line = UiFactory.NewImage(name, body, colour, UiSprites.Solid);
            var rect = (RectTransform)line.transform;
            rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rect.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rect.sizeDelta = new Vector2(-FrameInset * 2f, height);
            rect.anchoredPosition = new Vector2(0f, top ? -FrameInset : FrameInset);
        }

        /// <summary>A soft shadow the size of the card plus its reach, one elevation's alpha, a little below.</summary>
        private static Image Shadow(RectTransform root, UiElevation elevation)
        {
            float reach = UiElevationSpec.ShadowRadius(elevation);
            Image shadow = UiFactory.NewImage("Shadow" + (int)elevation, root, UiColor.ShadowKey, UiSprites.SoftShadow);
            shadow.raycastTarget = false;
            shadow.type = Image.Type.Sliced;
            shadow.pixelsPerUnitMultiplier = UiSprites.SoftShadowReach / Mathf.Max(1f, reach);
            RectTransform rect = UiFactory.Fill((RectTransform)shadow.transform, -reach);
            rect.anchoredPosition = new Vector2(0f, -reach * 0.25f);
            Color c = shadow.color;
            c.a = UiElevationSpec.ShadowAlpha(elevation);
            shadow.color = c;
            return shadow;
        }

        /// <summary>Lift on hover / focus (Elev2, y +4, Fast). Ignored while locked or when the card rests at Elev2.</summary>
        public void SetLifted(bool lifted)
        {
            if (_locked || _rest == UiElevation.Elev2)
            {
                lifted = false;
            }

            if (_lifted == lifted)
            {
                return;
            }

            _lifted = lifted;
            UiMotion.Move(_body, new Vector2(0f, lifted ? LiftPx : 0f), UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
            if (_fill != null)
            {
                UiMotion.Tint(_fill, UiTokens.Get(lifted ? UiColor.SurfaceRaised : _surface), UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
            }
            UiMotion.TintAlpha(_liftShadow, lifted ? 1f : 0f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
            UiMotion.TintAlpha(_restShadow, lifted || _rest == UiElevation.Elev0 ? 0f : 1f, UiDuration.Fast, UiEase.Settle, UiMotionClass.Decor);
        }

        /// <summary>A locked card stays down (and drops if it was lifted).</summary>
        public void SetLocked(bool locked)
        {
            _locked = locked;
            if (locked)
            {
                SetLifted(false);
            }
        }

        /// <summary>Lift with a button's hover / focus (the card's selectable surface).</summary>
        public void Bind(UiButton button)
        {
            if (button != null)
            {
                button.LiftChanged = SetLifted;
            }
        }
    }
}
