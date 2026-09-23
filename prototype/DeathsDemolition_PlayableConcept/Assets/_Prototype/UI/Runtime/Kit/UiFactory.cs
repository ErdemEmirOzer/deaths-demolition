using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>Visual role of a control. Decides fill, stroke and label tokens - never a literal.</summary>
    public enum UiButtonVariant
    {
        /// <summary>The one action a screen is about. Vermilion fill.</summary>
        Primary,

        /// <summary>Everything else on the same row. Outlined, no fill.</summary>
        Secondary,

        /// <summary>Left-hand navigation entry. Reads as a list row until it is focused.</summary>
        Nav,

        /// <summary>A selectable tile: skin swatch, weapon row.</summary>
        Card,

        /// <summary>Destructive confirmation. Primary geometry, danger fill.</summary>
        Danger
    }

    /// <summary>
    /// A top-down vertical cursor.
    ///
    /// Every screen in this module lays out by advancing this cursor by a
    /// <see cref="UiSpace"/> step and docking a fixed-height row. No auto-layout group,
    /// therefore no rebuild ordering to reason about, and - the reason that matters -
    /// the vertical rhythm of a screen is auditable by reading the call site: the sum
    /// of the gaps IS the spacing spec.
    /// </summary>
    public struct UiStack
    {
        public RectTransform Parent;

        /// <summary>Distance already consumed from the top of <see cref="Parent"/>, in reference px.</summary>
        public float Cursor;

        public UiStack(RectTransform parent, UiSpace topInset = UiSpace.None)
        {
            Parent = parent;
            Cursor = (int)topInset;
        }

        public void Gap(UiSpace amount)
        {
            Cursor += (int)amount;
        }

        /// <summary>Docks a child across the full width of the parent at the cursor, then advances.</summary>
        public RectTransform Add(RectTransform child, float height)
        {
            child.SetParent(Parent, false);
            child.anchorMin = new Vector2(0f, 1f);
            child.anchorMax = new Vector2(1f, 1f);
            child.pivot = new Vector2(0.5f, 1f);
            child.sizeDelta = new Vector2(0f, height);
            child.anchoredPosition = new Vector2(0f, -Cursor);
            Cursor += height;
            return child;
        }

        /// <summary>Docks a left-aligned child of a fixed width, then advances.</summary>
        public RectTransform AddLeft(RectTransform child, float width, float height)
        {
            child.SetParent(Parent, false);
            child.anchorMin = new Vector2(0f, 1f);
            child.anchorMax = new Vector2(0f, 1f);
            child.pivot = new Vector2(0f, 1f);
            child.sizeDelta = new Vector2(width, height);
            child.anchoredPosition = new Vector2(0f, -Cursor);
            Cursor += height;
            return child;
        }
    }

    /// <summary>
    /// The only place in the prototype that instantiates a UI GameObject.
    ///
    /// WHY A FACTORY AND NOT PREFABS
    /// -----------------------------
    /// A prefab stores a literal colour and a literal font size inside a .prefab file,
    /// where no audit can see it and no token can reach it. Every control here is
    /// assembled from <see cref="UiTokens"/> at runtime, so the audit in
    /// <see cref="UiContrast"/> measures the same numbers the screen actually draws and
    /// a palette change cannot leave a stale asset behind.
    ///
    /// It also means the module owns no binary asset at all, which keeps it inside its
    /// file ownership and lets it be built with Unity closed.
    ///
    /// NAMING: creators are prefixed New* where the obvious name would collide with the
    /// Unity type they return (NewImage, NewRect, NewSlider). A method called Image
    /// inside this class would hide the type Image from every declaration in it.
    /// </summary>
    public static class UiFactory
    {
        // =============================================================== screen roots

        /// <summary>
        /// A full-screen canvas at the reference resolution.
        ///
        /// <paramref name="uiCamera"/> null keeps the canvas in ScreenSpaceOverlay, which is
        /// what the HUD wants: overlay never moves with camera shake.
        ///
        /// Menu screens pass their own UI camera and get ScreenSpaceCamera instead, and the
        /// reason is a rendering fault, not a layout preference. This project's URP renderer
        /// runs with Intermediate Texture mode "Always": every camera draws into an
        /// off-screen colour target which is then copied to the back buffer. An overlay
        /// canvas is not part of that - the engine draws it straight onto the back buffer
        /// after the pipeline is finished. In the Run scene the world fills that buffer
        /// first, so nothing shows. A menu scene draws no geometry at all, and on a HiDPI
        /// surface the back buffer the overlay lands on is not the one URP prepared, so the
        /// menu appeared over whatever was left in that memory - the same UI, ghosted and
        /// smeared many times over. Routing menus through the UI camera puts them inside the
        /// pipeline's own target, which URP allocates and clears itself.
        /// </summary>
        public static Canvas CreateCanvas(string name, int sortingOrder, Transform parent = null,
            Camera uiCamera = null)
        {
            var go = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var canvas = go.GetComponent<Canvas>();
            if (uiCamera != null)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCamera;
                // Between the near and far planes set in EnsureUiCamera (0.1 .. 10).
                canvas.planeDistance = 1f;
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            canvas.sortingOrder = sortingOrder;
            canvas.pixelPerfect = false;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // WP-022: the player's UI scale (0.9-1.2) shrinks or grows the reference
            // frame; every layout below keeps its reference-pixel numbers.
            scaler.referenceResolution = UiTokens.ReferenceResolution / UiSettings.UiScale;

            // Match on height. These layouts are height-critical - the results screen is
            // required to fit without scrolling - and width-tolerant, because a wider
            // screen only gives the columns more air.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            scaler.referencePixelsPerUnit = 100f;

            return canvas;
        }

        /// <summary>
        /// Guarantees exactly one EventSystem. Keyboard and gamepad focus - and therefore
        /// the focus indicator acceptance criterion O4 is about - do not exist without it.
        /// </summary>
        public static EventSystem EnsureEventSystem()
        {
            EventSystem existing = UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (existing != null)
            {
                return existing;
            }

            // Active Input Handling is "Both" in this project (ProjectSettings
            // activeInputHandler: 2), so the legacy module is available. Using it keeps
            // this module free of any UnityEngine.InputSystem reference, which
            // Core/Input/IInputSource.cs requires of everything outside _Prototype/Input.
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            return go.GetComponent<EventSystem>();
        }

        /// <summary>
        /// A menu scene has no gameplay camera, and a scene with no camera at all makes
        /// Unity log "No cameras rendering", which acceptance criterion 27 counts as
        /// console noise. This clears the frame and draws no geometry.
        /// </summary>
        public static Camera EnsureUiCamera()
        {
            // WP-068 slice 0 (D5): the screen camera, never an off-screen one. The array
            // order of FindObjectsOfType is unspecified, so the first camera could be a
            // render-texture camera (a Selection turntable, a capture probe) and the menu
            // canvas would draw into that texture instead of the screen. Prefer the tagged
            // main camera, then any enabled camera drawing to the screen; else make one.
            Camera main = Camera.main;
            if (main != null && main.targetTexture == null)
            {
                return main;
            }

            Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
            for (int i = 0; cameras != null && i < cameras.Length; i++)
            {
                if (cameras[i] != null && cameras[i].enabled && cameras[i].targetTexture == null)
                {
                    return cameras[i];
                }
            }

            var go = new GameObject("UI Camera", typeof(Camera));
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = UiTokens.Get(UiColor.SurfaceBase);
            // Was 0, back when this camera existed only to clear the frame and Unity's one
            // complaint was "No cameras rendering". It now also draws the menu canvas, which
            // is rendered as part of whatever the camera can see, so a mask of nothing draws
            // nothing. Menu scenes contain no geometry, so seeing everything costs nothing.
            cam.cullingMask = ~0;
            cam.orthographic = true;
            cam.orthographicSize = 1f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 10f;
            cam.depth = -50f;
            // HDR and MSAA are left at their defaults so this camera's targets match what
            // the pipeline asset asks for. Overriding them made the UI camera's colour
            // target disagree with the rest of the frame for no measured saving.

            if (UnityEngine.Object.FindObjectOfType<AudioListener>() == null)
            {
                go.AddComponent<AudioListener>();
            }

            return cam;
        }

        // ================================================================ primitives

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
            return rt;
        }

        public static Image NewImage(string name, Transform parent, UiColor color, Sprite sprite = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            var image = go.GetComponent<Image>();
            image.sprite = sprite != null ? sprite : UiSprites.Solid;
            image.color = UiTokens.Get(color);
            image.raycastTarget = false;

            if (sprite != null && sprite.border != Vector4.zero)
            {
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1f;
            }

            return image;
        }

        /// <summary>Stretches a rect over its parent, inset by an equal margin on all sides.</summary>
        public static RectTransform Fill(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        /// <summary>Stretches a rect over its parent and OUTSIDE it by <paramref name="outset"/>.</summary>
        public static RectTransform Outset(RectTransform rt, float outset)
        {
            return Fill(rt, -outset);
        }

        /// <summary>
        /// A card: an outer rounded rect in the border token with the fill inset by one
        /// hairline. That is how every bordered surface here gets an outline without an
        /// outline sprite and without a second material.
        /// </summary>
        public static RectTransform Card(string name, Transform parent, UiColor border, UiColor fill)
        {
            Image outer = NewImage(name, parent, border, UiSprites.Card);
            Image inner = NewImage("Fill", outer.transform, fill, UiSprites.Card);
            Fill((RectTransform)inner.transform, UiTokens.StrokeHairline);
            return (RectTransform)outer.transform;
        }

        /// <summary>
        /// A card wearing an AP-012 ink frame (WP-022). The token fill stays underneath -
        /// contrast is measured against it, not against the line work - and the frame is
        /// added last so nothing built into the card can cover its corners.
        /// </summary>
        public static RectTransform FramedCard(
            string name, Transform parent, UiColor border, UiColor fill, UiFrame frame,
            UiColor frameColor = UiColor.TextSecondary)
        {
            RectTransform card = Card(name, parent, border, fill);
            UiArt.FrameOverlay(card, frame, frameColor);
            return card;
        }

        /// <summary>Padded content area of a <see cref="Card"/>.</summary>
        public static RectTransform CardContent(RectTransform card, UiSpace padding = UiSpace.XL)
        {
            RectTransform content = NewRect("Content", card);
            Fill(content, (int)padding);
            return content;
        }

        // ================================================================ typography

        /// <summary>
        /// One-line box height for a type step, rounded up to the 4pt sub-grid.
        ///
        /// The 1.25 factor is not padding taste, it is correctness: uGUI lays out at
        /// <c>font.lineHeight * lineSpacing</c>, and a typical face's line height is
        /// about 1.15x its point size. A box computed as fontSize * lineSpacing is
        /// therefore SHORTER than one line - and a Text set to Truncate that cannot fit
        /// a single line renders NOTHING AT ALL. That failure is silent, looks like a
        /// data bug, and would hit the largest type first.
        /// </summary>
        /// <summary>
        /// WP-068 slice 0 (D6): a fixed text band (chip, row, title band) at the current
        /// reading-text scale. Identical at TextScale 1.0; 25 % taller at 1.25, so a band
        /// sized for one line of Body/Label/Caption never clips that line when the player
        /// raises the text scale (Accessibility pane).
        /// </summary>
        public static TMP_Text AllowRichText(TMP_Text label)
        {
            // WP-068 slice 0 (D7): labels are created with rich text OFF (a user-facing
            // string can never inject tags); a label that deliberately carries inline
            // markup - a colour run, an inline glyph - opts in here, one label at a time.
            if (label != null)
            {
                label.richText = true;
            }

            return label;
        }

        public static float ScaledBand(float band)
        {
            return Mathf.Ceil(band * Mathf.Clamp(UiTokens.TextScale, 1f, 1.25f));
        }

        public static float LineHeight(UiType type)
        {
            float raw = UiTokens.FontSize(type) * UiTokens.LineSpacing(type) * 1.25f;
            return Mathf.Ceil(raw / 4f) * 4f;
        }

        /// <summary>
        /// A text block (WP-022B: TextMeshProUGUI on the project's TMP font asset). The
        /// signature keeps uGUI's <see cref="TextAnchor"/> so twenty screens did not have
        /// to learn a second alignment enum; <see cref="Align"/> translates it.
        /// </summary>
        public static TMP_Text Label(
            string name,
            Transform parent,
            string content,
            UiType type,
            UiColor color,
            TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;

            var text = go.GetComponent<TextMeshProUGUI>();
            TMP_FontAsset font = UiFonts.Primary;
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize = UiTokens.FontSize(type);
            text.fontStyle = UiTokens.Weight(type) == FontStyle.Bold ? FontStyles.Bold : FontStyles.Normal;
            // TMP line spacing is an offset in font units, not a multiplier: the extra air
            // long-form text gets is (multiplier - 1) of the em, expressed in TMP's units.
            text.lineSpacing = (UiTokens.LineSpacing(type) - 1f) * 100f;
            text.color = UiTokens.Get(color);
            text.alignment = Align(anchor);
            text.raycastTarget = false;
            text.richText = false;
            text.extraPadding = true;

            // O7 - overflow behaviour. Every text box here has a fixed height, so wrap
            // plus truncation means content longer than its box is cut at a line
            // boundary. Nothing in this module can push a neighbour out of place.
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Truncate;

            text.text = content ?? string.Empty;
            return text;
        }

        /// <summary>uGUI anchor to TMP alignment: the nine cells map one to one.</summary>
        public static TextAlignmentOptions Align(TextAnchor anchor)
        {
            switch (anchor)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                default: return TextAlignmentOptions.BottomRight;
            }
        }

        /// <summary>
        /// A readout that must never wrap or clip: it shrinks to fit instead. Used where
        /// the value can grow beyond its design width - gold, kill counts, timers.
        /// </summary>
        public static TMP_Text Numeric(
            string name,
            Transform parent,
            string content,
            UiType type,
            UiColor color,
            TextAnchor anchor = TextAnchor.MiddleLeft,
            bool autoSize = true)
        {
            TMP_Text text = Label(name, parent, content, type, color, anchor);
            text.enableWordWrapping = false;
            // WP-022K: auto-sizing re-runs TMP's mesh generation several times per text
            // change and allocates on every pass (HUD low-health window: ~9 KB per changed
            // readout, 400 B/frame while health regenerates). A readout whose value is
            // bounded by design (HUD health / ammo / gold / round / timer) passes
            // autoSize:false and keeps one fixed size.
            text.enableAutoSizing = autoSize;
            text.fontSizeMax = UiTokens.FontSize(type);
            text.fontSizeMin = autoSize ? Mathf.Max(12, UiTokens.FontSize(type) / 2) : UiTokens.FontSize(type);
            return text;
        }

        /// <summary>
        /// Section heading: vermilion tick, then the label. The one recurring decorative
        /// device in the module - a torii upright, not a stock ornament - and the reason
        /// four different screens read as one product.
        /// </summary>
        public static RectTransform Heading(string text, Transform parent, UiType type = UiType.H2)
        {
            RectTransform row = NewRect("Heading", parent);

            Image tick = NewImage("Tick", row, UiColor.AccentPrimary, UiSprites.Chip);
            var tickRect = (RectTransform)tick.transform;
            tickRect.anchorMin = new Vector2(0f, 0.5f);
            tickRect.anchorMax = new Vector2(0f, 0.5f);
            tickRect.pivot = new Vector2(0f, 0.5f);
            tickRect.sizeDelta = new Vector2(UiTokens.HeadingTickWidth, UiTokens.HeadingTickHeight);
            tickRect.anchoredPosition = Vector2.zero;

            TMP_Text label = Label("Label", row, text, type, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform labelRect = Fill((RectTransform)label.transform);
            labelRect.offsetMin = new Vector2(UiTokens.HeadingTickWidth + (int)UiSpace.M, 0f);

            return row;
        }

        /// <summary>Hairline rule. Decorative, therefore exempt from the 3:1 boundary rule.</summary>
        public static Image Divider(string name, Transform parent)
        {
            return NewImage(name, parent, UiColor.BorderHairline);
        }

        /// <summary>
        /// Key/value row. Every statistic on every screen is one of these, which is what
        /// makes the profile pane, the statistics pane and the results screen relatives
        /// rather than three separate designs.
        /// </summary>
        public static TMP_Text StatRow(
            string key,
            string value,
            Transform parent,
            UiColor valueColor = UiColor.TextPrimary,
            UiType valueType = UiType.BodyStrong,
            float valueWidth = 200f)
        {
            RectTransform row = NewRect("Row", parent);

            TMP_Text keyText = Label("Key", row, key, UiType.Body, UiColor.TextSecondary, TextAnchor.MiddleLeft);
            RectTransform keyRect = Fill((RectTransform)keyText.transform);
            keyRect.offsetMax = new Vector2(-valueWidth, 0f);

            TMP_Text valueText = Label("Value", row, value, valueType, valueColor, TextAnchor.MiddleRight);
            Fill((RectTransform)valueText.transform);

            return valueText;
        }

        /// <summary>Small pill used for states: LOCKED, EARNED, NEW, SUSPENDED.</summary>
        /// <summary>AD v004 SHOULD-5 chip system - two heights only: card chips (NEW / EARNED / IN PROGRESS / LOCKED / SELECTED) and row chips (UNLOCKED / LEVEL n / condition).</summary>
        public const float CardChipHeight = 28f;
        public const float RowChipHeight = 22f;

        public static TMP_Text Chip(
            string name,
            Transform parent,
            string content,
            UiColor fill,
            UiColor textColor,
            float width = 120f,
            float height = 28f)
        {
            Image background = NewImage(name, parent, fill, UiSprites.Chip);
            var rt = (RectTransform)background.transform;
            rt.sizeDelta = new Vector2(width, height);

            TMP_Text text = Label("Text", rt, content, UiType.Label, textColor, TextAnchor.MiddleCenter);

            // Horizontal inset only. A vertical inset on a 24 px pill would leave 16 px
            // for an 18 px line, and a truncating Text that cannot fit one line draws
            // nothing - the pill would be a blank lozenge.
            RectTransform textRect = Fill((RectTransform)text.transform);
            textRect.offsetMin = new Vector2((int)UiSpace.S, 0f);
            textRect.offsetMax = new Vector2(-(int)UiSpace.S, 0f);
            return text;
        }

        /// <summary>
        /// Clips a container to its own rect.
        ///
        /// The structural guarantee behind O7: a card with this on it cannot spill,
        /// whatever content it is handed. Combined with the truncating text boxes it
        /// means the only two possible outcomes for over-long content are "shortened"
        /// and "cut at the card edge" - never "pushes the layout apart", and never
        /// "grows a scrollbar", which the results screen is explicitly not allowed to do.
        /// </summary>
        public static RectTransform ClipContent(RectTransform rt)
        {
            if (rt != null && rt.GetComponent<RectMask2D>() == null)
            {
                rt.gameObject.AddComponent<RectMask2D>();
            }

            return rt;
        }

        // =================================================================== controls

        /// <summary>
        /// Builds a control with its full state stack: transparent raycast surface,
        /// focus halo, focus ring, border, fill, label.
        ///
        /// The focus ring is a CHILD of the button, outset beyond it, and the visible
        /// fill is a later child drawn on top - so the ring shows as a band around the
        /// control. A sibling ring would have to track the button's rect by hand.
        /// </summary>
        public static UiButton Button(
            string name,
            Transform parent,
            string content,
            UiButtonVariant variant,
            Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(UiButton));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(320f, UiTokens.TargetPreferred);
            rt.anchoredPosition = Vector2.zero;

            // Invisible but raycastTarget: the hit area is the whole control rect, so O3
            // is satisfied by the rect itself and not by whatever the visible fill is.
            var surface = go.GetComponent<Image>();
            surface.sprite = UiSprites.Solid;
            surface.color = UiTokens.Get(UiColor.Transparent);
            surface.raycastTarget = true;

            Sprite shape = variant == UiButtonVariant.Card ? UiSprites.Card : UiSprites.Control;

            Image halo = NewImage("FocusHalo", rt, UiColor.BorderFocusHalo, shape);
            Outset((RectTransform)halo.transform, UiTokens.StrokeFocusRing + UiTokens.StrokeFocusHalo);

            Image ring = NewImage("FocusRing", rt, UiColor.BorderFocus, shape);
            Outset((RectTransform)ring.transform, UiTokens.StrokeFocusRing);

            Image border = NewImage("Border", rt, UiColor.BorderStrong, shape);
            Fill((RectTransform)border.transform);

            Image fill = NewImage("Fill", rt, UiColor.SurfaceBase, shape);
            Fill((RectTransform)fill.transform, UiTokens.StrokeControl);

            // WP-022: with the AP-012 atlas present the border becomes the ink frame
            // (line only, drawn over the fill); the colour logic in UiButton is untouched,
            // it still tints the same "Border" graphic per state.
            UiFrame frameKind = variant == UiButtonVariant.Card ? UiFrame.Card : UiFrame.Button;
            if (UiArt.HasFrame(frameKind))
            {
                border.sprite = UiArt.Frame(frameKind);
                border.type = Image.Type.Sliced;
                border.fillCenter = false;
                border.pixelsPerUnitMultiplier = UiArt.FramePixelsPerUnitMultiplier;
                Fill((RectTransform)fill.transform, UiTokens.StrokeControl);
                border.transform.SetSiblingIndex(fill.transform.GetSiblingIndex() + 1);
            }

            TextAnchor anchor = variant == UiButtonVariant.Nav || variant == UiButtonVariant.Card
                ? TextAnchor.MiddleLeft
                : TextAnchor.MiddleCenter;

            TMP_Text label = Label("Label", rt, content, UiType.BodyStrong, UiColor.TextPrimary, anchor);
            RectTransform labelRect = Fill((RectTransform)label.transform);
            labelRect.offsetMin = new Vector2((int)UiSpace.L, 0f);
            labelRect.offsetMax = new Vector2(-(int)UiSpace.L, 0f);

            var button = go.GetComponent<UiButton>();
            button.Configure(variant, surface, halo, ring, border, fill, label, onClick);
            return button;
        }

        /// <summary>
        /// A horizontal bar. The track is a rounded well; the fill is a solid sprite in
        /// Filled mode, so a partial fill never distorts a corner radius.
        /// </summary>
        public static UiBar Bar(string name, Transform parent, UiColor fillColor, float height = 16f)
        {
            RectTransform root = NewRect(name, parent);
            root.sizeDelta = new Vector2(240f, height);

            Image track = NewImage("Track", root, UiColor.BarTrack, UiSprites.Control);
            Fill((RectTransform)track.transform);

            Image fill = NewImage("Fill", root, fillColor);
            Fill((RectTransform)fill.transform, 2f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;

            var bar = root.gameObject.AddComponent<UiBar>();
            bar.Configure(fill, fillColor);
            return bar;
        }

        /// <summary>
        /// Horizontal slider with a 44 px handle and an annulus focus ring. Built by hand
        /// rather than via DefaultControls, so every colour is a token and the focus
        /// indicator exists.
        /// </summary>
        public static Slider NewSlider(string name, Transform parent, float min, float max, float value)
        {
            RectTransform root = NewRect(name, parent);
            root.sizeDelta = new Vector2(360f, UiTokens.TargetCompact);

            Image track = NewImage("Track", root, UiColor.BarTrack, UiSprites.Control);
            var trackRect = (RectTransform)track.transform;
            trackRect.anchorMin = new Vector2(0f, 0.5f);
            trackRect.anchorMax = new Vector2(1f, 0.5f);
            trackRect.pivot = new Vector2(0.5f, 0.5f);
            trackRect.sizeDelta = new Vector2(0f, 10f);
            trackRect.anchoredPosition = Vector2.zero;

            // Unity's Slider rewrites the fill rect's anchors every frame, so the fill
            // area - not the fill - is what defines the bar's height.
            RectTransform fillArea = NewRect("FillArea", root);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.pivot = new Vector2(0.5f, 0.5f);
            fillArea.sizeDelta = new Vector2(-UiTokens.TargetCompact, 10f);
            fillArea.anchoredPosition = Vector2.zero;

            Image fill = NewImage("Fill", fillArea, UiColor.TextPrimary, UiSprites.Control); // AD v003 MUST-2: cream, vermilion is CTA/danger only
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.sizeDelta = Vector2.zero;
            fillRect.anchoredPosition = Vector2.zero;

            RectTransform handleArea = NewRect("HandleArea", root);
            Fill(handleArea);
            handleArea.offsetMin = new Vector2(UiTokens.TargetCompact * 0.5f, 0f);
            handleArea.offsetMax = new Vector2(-UiTokens.TargetCompact * 0.5f, 0f);

            // AD v003 MUST-2: cream knob with a 2 px ink edge (the disc is the edge, the
            // inset child is the knob) so it separates from the cream fill.
            Image handle = NewImage("Handle", handleArea, UiColor.SurfacePanel, UiSprites.Disc);
            var handleRect = (RectTransform)handle.transform;
            handleRect.sizeDelta = new Vector2(UiTokens.TargetCompact, 0f);
            handle.raycastTarget = true;
            Image knob = NewImage("Knob", handleRect, UiColor.TextPrimary, UiSprites.Disc);
            Fill((RectTransform)knob.transform);
            ((RectTransform)knob.transform).offsetMin = new Vector2(2f, 2f);
            ((RectTransform)knob.transform).offsetMax = new Vector2(-2f, -2f);

            // Annulus, so the handle stays visible through its own focus ring.
            Image ringHalo = NewImage("FocusHalo", handleRect, UiColor.BorderFocusHalo, UiSprites.Ring);
            Outset((RectTransform)ringHalo.transform, UiTokens.StrokeFocusRing + UiTokens.StrokeFocusHalo);

            Image ring = NewImage("FocusRing", handleRect, UiColor.BorderFocus, UiSprites.RingThin);
            Outset((RectTransform)ring.transform, UiTokens.StrokeFocusRing);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.transition = Selectable.Transition.None;
            slider.value = Mathf.Clamp(value, min, max);

            root.gameObject.AddComponent<UiSliderFocus>().Configure(slider, ringHalo.gameObject, ring.gameObject);
            return slider;
        }

        // ==================================================================== helpers

        /// <summary>Positions a rect by explicit reference-pixel offsets from the top-left.</summary>
        public static RectTransform Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(x, -y);
            return rt;
        }

        /// <summary>Docks a rect to a screen corner with the safe inset.</summary>
        public static RectTransform Corner(
            RectTransform rt,
            Vector2 anchor,
            float width,
            float height,
            UiSpace inset = UiTokens.SafeInset)
        {
            float pad = (int)inset;
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(
                Mathf.Approximately(anchor.x, 0f) ? pad : Mathf.Approximately(anchor.x, 1f) ? -pad : 0f,
                Mathf.Approximately(anchor.y, 0f) ? pad : Mathf.Approximately(anchor.y, 1f) ? -pad : 0f);
            return rt;
        }

        /// <summary>
        /// Clamps a string and appends an ellipsis. Applied to every value that comes
        /// from outside this module - build strings, weapon names, source ids - so a
        /// surprise 200-character value truncates instead of reflowing a card.
        /// </summary>
        public static string Clip(string value, int maxCharacters)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (value.Length <= maxCharacters)
            {
                return value;
            }

            return value.Substring(0, Mathf.Max(1, maxCharacters - 1)) + "…";
        }

        /// <summary>Adds a CanvasGroup if one is not already there.</summary>
        public static CanvasGroup Group(GameObject go)
        {
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            return group != null ? group : go.AddComponent<CanvasGroup>();
        }
    }
}
