using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 5 · component #5 UiSlider v3 (direction §4): a settings row - label, a
    /// 6 px rail with a cream fill, a 28 px lacquer bead (2 px ink edge; 44 px hit), optional
    /// detent ticks, and the value as text at the right.
    ///
    /// Its own Selectable rather than uGUI's Slider, because uGUI rewrites the handle anchors
    /// on every value change and the bead could never move on the UiMotion core. Drag is 1:1
    /// (pointer = bead, no easing); a keyboard / pad step (← / →) changes the label at once
    /// and moves the bead and fill in 140 ms (Move, Decor). <see cref="UiEvent.SliderChanged"/>
    /// is raised at most 12 times a second (the tick sound's ceiling).
    ///
    /// USAGE: a continuous or finely stepped value (volumes, UI scale, camera shake, dead
    /// zone). DON'T: use it for fewer than 5 values (that is <see cref="UiStepper"/>), or
    /// hide the number - the bead position is never the only readout.
    /// ACCESSIBILITY: the value is always written; ← / → step, ↑ / ↓ leave the row; the focus
    /// ring sits on the bead (Focus400 + halo) and the row gets the travelling brackets;
    /// Reduced Motion lands a step at once (Decor).
    ///
    /// WP-098A P0b (C-00 07.10) · C14 Brush style (<see cref="UiControlStyle.Brush"/>, KILAVUZ §4.1 / KIT-API §4 "Ayar
    /// satırları"): the frameless row - MenuBody label in the label column, then a 1 px rail in the line colour, a 3 px
    /// cream fill (AP-034's SelectLong mask when authored, else Solid) and a 12 px cream head with a 2 px ink edge, and the
    /// value in NumericL at the right. No ticks, no bead ring: focus is the row's brackets (C7). Disabled: fill, label and
    /// value TextSecondary, no head. The row is <see cref="UiBrushTokens.RowHeight"/>(MenuBody, RowHit) high - measured
    /// from the text, so it grows at TextScale 1.25. V3 (the default) is unchanged.
    /// </summary>
    public sealed class UiSlider : Selectable, IPointerDownHandler, IDragHandler, IInitializePotentialDragHandler
    {
        public const float RowHeight = 48f;
        public const float RailHeight = 6f;
        public const float BeadSize = 28f;
        public const float HitSize = 44f;
        public const float ValueWidth = 88f;
        public const float MaxEventsPerSecond = 12f;

        /// <summary>Label x inside the row: the same as a framed row's (UiSwitch / UiStepper label offset XL − 4).</summary>
        public const float LabelInset = (int)UiSpace.XL - 4f;

        /// <summary>
        /// 12b (AD R1-A SHOULD-7): an even-step slider with more than this many stops is a ruler
        /// (LOOK SENSITIVITY 1–20, HEAD BOB 5 %): its unit ticks are <see cref="MinorTickWidth"/>
        /// and every <see cref="MajorTickEvery"/>th unit (value-anchored: 5, 10, 15 / 25, 50, 75 %)
        /// is <see cref="MajorTickWidth"/> - the health strong-tick rule. Fewer stops (UI SCALE's
        /// six, the uneven TEXT SCALE detents) keep 2 px detents (AD 5b MUST-3).
        /// </summary>
        public const int RulerStops = 10;
        public const int MajorTickEvery = 5;
        public const float MinorTickWidth = 1f;
        public const float MajorTickWidth = 2f;

        private RectTransform _rail;
        private RectTransform _fill;
        private RectTransform _bead;
        private GameObject _ringHalo;
        private GameObject _ring;
        private TMP_Text _value;
        private float _min;
        private float _max = 1f;
        private float _step;
        private float[] _stops;
        private float _current;
        private Func<float, string> _format;
        private Action<float> _changed;
        private float _lastRaise = -1f;
        private bool _built;

        // WP-098A P0b · Brush style only (V3 leaves them null / 0).
        private UiControlStyle _style;
        private TMP_Text _caption;
        private Image _fillImage;
        private float _fillHeight;   // 0 = the fill stretches over the rail's height (V3); Brush: SliderFill over a 1 px rail

        /// <summary>WP-098A: the dialect this row is drawn in.</summary>
        public UiControlStyle Style
        {
            get { return _style; }
        }

        /// <summary>WP-098A: the row's label (tests: T3 / U2 measurement at TextScale 1.25).</summary>
        public TMP_Text Label
        {
            get { return _caption; }
        }

        /// <summary>WP-098A: the value label (tests: the widest value fits its box).</summary>
        public TMP_Text ValueLabel
        {
            get { return _value; }
        }

        /// <summary>WP-098A Brush: the head shows (false while the row is disabled - KILAVUZ §4.1 C14).</summary>
        public bool HeadVisible
        {
            get { return _bead != null && _bead.gameObject.activeSelf; }
        }

        public float Value
        {
            get { return _current; }
        }

        public string ValueText
        {
            get { return _value != null ? _value.text : string.Empty; }
        }

        /// <summary>Bead centre x in the rail (test readout).</summary>
        public float BeadX
        {
            get { return _bead != null ? _bead.anchoredPosition.x : 0f; }
        }

        public float RailWidth
        {
            get { return _rail != null ? _rail.rect.width : 0f; }
        }

        /// <summary>
        /// Build a slider row. <paramref name="step"/> 0 = continuous (keyboard steps 1/20 of
        /// the range); <paramref name="format"/> writes the value text (default: whole percent).
        /// WP-098A: <paramref name="style"/> Brush draws the C14 row (<see cref="UiBrushKit.Slider"/> is its entry point;
        /// <paramref name="labelWidth"/> is then the label column); V3 is today's row.
        /// </summary>
        public static UiSlider Create(string name, Transform parent, string label, float min, float max, float value, float step,
            Func<float, string> format, Action<float> changed, float width = 640f, float labelWidth = 240f,
            UiControlStyle style = UiControlStyle.V3)
        {
            RectTransform root = UiFactory.NewRect(name, parent);
            root.sizeDelta = new Vector2(width, style == UiControlStyle.Brush ? BrushRowHeight() : RowHeight);
            Image hit = UiFactory.NewImage("Hit", root, UiColor.Transparent, UiSprites.Solid);
            UiFactory.Fill((RectTransform)hit.transform);
            hit.raycastTarget = true;

            var slider = root.gameObject.AddComponent<UiSlider>();
            slider._style = style;
            slider.Build(root, hit, label, min, max, value, step, format, changed, labelWidth);
            return slider;
        }

        /// <summary>WP-098A: a C13 row's height at the current text scale (KILAVUZ §2.3: measured from MenuBody, never under RowHit).</summary>
        public static float BrushRowHeight()
        {
            return UiBrushTokens.RowHeight(UiType.MenuBody, UiBrushTokens.RowHit);
        }

        private void Build(RectTransform root, Image hit, string label, float min, float max, float value, float step,
            Func<float, string> format, Action<float> changed, float labelWidth)
        {
            _min = min;
            _max = Mathf.Max(min + 1e-4f, max);
            _step = Mathf.Max(0f, step);
            _format = format;
            _changed = changed;
            transition = Transition.None;
            targetGraphic = hit;

            if (_style == UiControlStyle.Brush)
            {
                BuildBrush(root, label, labelWidth);
                _built = true;
                SetValue(value, false, false);
                ApplyBrushState();
                return;
            }

            TMP_Text caption = UiFactory.Label("Label", root, label, UiType.Body, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            _caption = caption;
            RectTransform captionRect = caption.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 0f);
            captionRect.anchorMax = new Vector2(0f, 1f);
            captionRect.pivot = new Vector2(0f, 0.5f);
            captionRect.sizeDelta = new Vector2(labelWidth, 0f);
            captionRect.anchoredPosition = new Vector2(LabelInset, 0f);   // the framed rows' label x (AD 6ab SHOULD-4)

            // The value in the Numeric face (tabular: "80%" / "1.00×" do not jitter while dragging; AD 5b SHOULD-4 / 3 SHOULD-2).
            _value = UiFactory.Numeric("Value", root, string.Empty, UiType.NumericM, UiColor.TextPrimary, TextAnchor.MiddleRight, false);
            RectTransform valueRect = _value.rectTransform;
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.sizeDelta = new Vector2(ValueWidth, 0f);
            valueRect.anchoredPosition = new Vector2(-(int)UiSpace.L, 0f);

            // Rail between the label and the value, inset by half a bead so the bead never overhangs.
            float left = LabelInset + labelWidth + (int)UiSpace.M + BeadSize * 0.5f;
            float right = (int)UiSpace.L + ValueWidth + (int)UiSpace.M + BeadSize * 0.5f;
            // Rail = SurfaceInset + 1 px BorderHairline rim (the UiBar v3 track recipe): a BarTrack
            // rail on SurfaceBase is ΔL* ≈ 2 and the extent vanished at 0 % (AD 5b MUST-3).
            Image rail = UiFactory.NewImage("Rail", root, UiColor.SurfaceInset, UiSprites.Chip);
            _rail = (RectTransform)rail.transform;
            _rail.anchorMin = new Vector2(0f, 0.5f);
            _rail.anchorMax = new Vector2(1f, 0.5f);
            _rail.pivot = new Vector2(0f, 0.5f);
            _rail.offsetMin = new Vector2(left, -RailHeight * 0.5f);
            _rail.offsetMax = new Vector2(-right, RailHeight * 0.5f);
            Image railRim = UiFactory.NewImage("Rim", _rail, UiColor.BorderHairline, UiSprites.ChipRim);
            UiFactory.Fill((RectTransform)railRim.transform, -1f);

            // Fill = a sliced pill whose WIDTH follows the value (a Filled image would stretch the
            // rounded sprite into a taper); it moves with the bead on the Size channel.
            Image fill = UiFactory.NewImage("Fill", _rail, UiColor.TextPrimary, UiSprites.Chip); // AD v003 MUST-2: cream, never vermilion
            _fill = (RectTransform)fill.transform;
            _fill.anchorMin = new Vector2(0f, 0f);
            _fill.anchorMax = new Vector2(0f, 1f);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.anchoredPosition = Vector2.zero;

            int stops = _step > 0f ? Mathf.RoundToInt((_max - _min) / _step) : 0;
            if (stops >= 2 && stops <= 20)
            {
                bool ruler = stops > RulerStops;
                for (int i = 1; i < stops; i++)
                {
                    AddTick(i, (float)i / stops, ruler && !IsMajorUnit(_min + i * _step, _step) ? MinorTickWidth : MajorTickWidth);
                }
            }

            // Bead: ink edge disc + cream core, focus ring + halo around it; the hit is the 44 px rect.
            Image edge = UiFactory.NewImage("Bead", _rail, UiColor.SurfacePanel, UiSprites.Disc);
            _bead = (RectTransform)edge.transform;
            _bead.anchorMin = new Vector2(0f, 0.5f);
            _bead.anchorMax = new Vector2(0f, 0.5f);
            _bead.pivot = new Vector2(0.5f, 0.5f);
            _bead.sizeDelta = new Vector2(BeadSize, BeadSize);
            Image core = UiFactory.NewImage("Core", _bead, UiColor.TextPrimary, UiSprites.Disc);
            UiFactory.Fill((RectTransform)core.transform, 2f);
            Image halo = UiFactory.NewImage("FocusHalo", _bead, UiColor.BorderFocusHalo, UiSprites.Ring);
            UiFactory.Fill((RectTransform)halo.transform, -(UiTokens.StrokeFocusRing + UiTokens.StrokeFocusHalo));
            Image ring = UiFactory.NewImage("FocusRing", _bead, UiColor.BorderFocus, UiSprites.RingThin);
            UiFactory.Fill((RectTransform)ring.transform, -UiTokens.StrokeFocusRing);
            _ringHalo = halo.gameObject;
            _ringHalo.transform.SetSiblingIndex(0);
            _ring = ring.gameObject;
            _ringHalo.SetActive(false);
            _ring.SetActive(false);

            _built = true;
            SetValue(value, false, false);
        }

        /// <summary>
        /// WP-098A P0b · the C14 row (KIT-API §4 "Ayar satırları"): label column | rail · fill · head | value. The value box
        /// is as wide as the widest of the range's two ends written by <see cref="_format"/> (never under
        /// <see cref="ValueWidth"/>, so stacked rows keep one rail end); the rail takes the rest of the value column.
        /// </summary>
        private void BuildBrush(RectTransform root, string label, float labelWidth)
        {
            _caption = UiFactory.Label("Label", root, label, UiType.MenuBody, UiColor.TextPrimary, TextAnchor.MiddleLeft);
            _caption.enableWordWrapping = false;
            RectTransform captionRect = _caption.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 0f);
            captionRect.anchorMax = new Vector2(0f, 1f);
            captionRect.pivot = new Vector2(0f, 0.5f);
            captionRect.sizeDelta = new Vector2(labelWidth, 0f);
            captionRect.anchoredPosition = Vector2.zero;   // frameless: the label stands on the row's own edge

            _value = UiFactory.Numeric("Value", root, string.Empty, UiType.NumericL, UiColor.TextPrimary, TextAnchor.MiddleRight, false);
            float valueWidth = Mathf.Max(ValueWidth, Mathf.Ceil(Mathf.Max(_value.GetPreferredValues(Format(_min)).x, _value.GetPreferredValues(Format(_max)).x)));
            RectTransform valueRect = _value.rectTransform;
            valueRect.anchorMin = new Vector2(1f, 0f);
            valueRect.anchorMax = new Vector2(1f, 1f);
            valueRect.pivot = new Vector2(1f, 0.5f);
            valueRect.sizeDelta = new Vector2(valueWidth, 0f);
            valueRect.anchoredPosition = Vector2.zero;

            // The rail spans the value column up to the value box, inset by half a head so the head never overhangs.
            float head = UiBrushTokens.SliderHead + 2f * UiTokens.StrokeControl;
            float left = labelWidth + UiBrushKit.ValueColumnGap + head * 0.5f;
            float right = valueWidth + (int)UiSpace.L + head * 0.5f;
            Image rail = UiFactory.NewImage("Rail", root, UiColor.TextMuted, UiSprites.Solid);
            rail.color = UiBrushTokens.LineColour();
            _rail = (RectTransform)rail.transform;
            _rail.anchorMin = new Vector2(0f, 0.5f);
            _rail.anchorMax = new Vector2(1f, 0.5f);
            _rail.pivot = new Vector2(0f, 0.5f);
            _rail.offsetMin = new Vector2(left, -UiBrushTokens.SliderRail * 0.5f);
            _rail.offsetMax = new Vector2(-right, UiBrushTokens.SliderRail * 0.5f);

            // Fill: cream (G15 - never gold, never vermilion), 3 px centred on the 1 px rail; its WIDTH follows the value.
            Sprite fillSprite = UiBrushArt.IsAuthored(UiBrushSprite.SelectLong) ? UiBrushArt.Get(UiBrushSprite.SelectLong) : UiSprites.Solid;
            _fillImage = UiFactory.NewImage("Fill", _rail, UiColor.TextPrimary, fillSprite);
            _fill = (RectTransform)_fillImage.transform;
            _fill.anchorMin = new Vector2(0f, 0.5f);
            _fill.anchorMax = new Vector2(0f, 0.5f);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.anchoredPosition = Vector2.zero;
            _fillHeight = UiBrushTokens.SliderFill;

            // Head: a 2 px ink edge round a 12 px cream core (KIT-API §4) - the cream reads on any ground, the edge on the fill.
            Sprite headSprite = UiBrushArt.Get(UiBrushSprite.SliderHead);
            Image edge = UiFactory.NewImage("Bead", _rail, UiColor.SurfaceInset, headSprite);
            _bead = (RectTransform)edge.transform;
            _bead.anchorMin = new Vector2(0f, 0.5f);
            _bead.anchorMax = new Vector2(0f, 0.5f);
            _bead.pivot = new Vector2(0.5f, 0.5f);
            _bead.sizeDelta = new Vector2(head, head);
            Image core = UiFactory.NewImage("Core", _bead, UiColor.TextPrimary, headSprite);
            UiFactory.Fill((RectTransform)core.transform, UiTokens.StrokeControl);
        }

        /// <summary>The value as <see cref="_format"/> writes it (build-time measuring only).</summary>
        private string Format(float v)
        {
            return _format != null ? _format(v) : UiStrings.Percent(Mathf.RoundToInt(Normalised(v) * 100f));
        }

        /// <summary>
        /// WP-098A Brush · the disabled pair (KILAVUZ §4.1 C14): label, fill and value TextSecondary, the head hidden. Runs on
        /// a state change only (DoStateTransition), never per frame; a colour is written only when it changes.
        /// </summary>
        private void ApplyBrushState()
        {
            if (!_built || _style != UiControlStyle.Brush)
            {
                return;
            }

            bool usable = IsInteractable();
            Color ink = UiTokens.Get(usable ? UiColor.TextPrimary : UiColor.TextSecondary);
            if (_caption.color != ink) _caption.color = ink;
            if (_value.color != ink) _value.color = ink;
            if (_fillImage.color != ink) _fillImage.color = ink;
            if (_bead.gameObject.activeSelf != usable) _bead.gameObject.SetActive(usable);
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);   // Transition.None: nothing of its own
            ApplyBrushState();
        }

        /// <summary>True when <paramref name="value"/> is a whole multiple of <see cref="MajorTickEvery"/> steps (a ruler's strong tick).</summary>
        public static bool IsMajorUnit(float value, float step)
        {
            if (step <= 0f)
            {
                return true;
            }

            return Mathf.RoundToInt(value / step) % MajorTickEvery == 0;
        }

        /// <summary>BorderStrong tick above the rail's rim at rail position <paramref name="t"/>, 2 px (AD 5b MUST-3) or a ruler's 1 px unit (AD R1-A SHOULD-7).</summary>
        private void AddTick(int index, float t, float width = MajorTickWidth)
        {
            if (_style == UiControlStyle.Brush)
            {
                return;   // C14 has no detent ticks: the value is written, the rail stays one quiet line
            }

            Image tick = UiFactory.NewImage("Tick" + index, _rail, UiColor.BorderStrong, UiSprites.Solid);
            var tickRect = (RectTransform)tick.transform;
            tickRect.anchorMin = new Vector2(t, 0.5f);
            tickRect.anchorMax = tickRect.anchorMin;
            tickRect.pivot = new Vector2(0.5f, 0.5f);
            tickRect.sizeDelta = new Vector2(width, RailHeight + 6f);
            if (_bead != null)
            {
                tick.transform.SetSiblingIndex(_bead.GetSiblingIndex()); // under the bead
            }
        }

        /// <summary>
        /// Uneven detents (TEXT SCALE 100 / 110 / 125 %, direction E8): the value snaps to the
        /// nearest stop, ← / → go stop to stop, and each inside stop gets a tick. Replaces the
        /// even step.
        /// </summary>
        public void SetStops(params float[] stops)
        {
            if (stops == null || stops.Length < 2)
            {
                return;
            }

            _stops = (float[])stops.Clone();
            Array.Sort(_stops);
            for (int i = _rail.childCount - 1; i >= 0; i--)
            {
                Transform child = _rail.GetChild(i);
                if (child.name.StartsWith("Tick", StringComparison.Ordinal))
                {
                    child.SetParent(null, false);
                    if (Application.isPlaying) Destroy(child.gameObject);
                    else DestroyImmediate(child.gameObject);
                }
            }

            for (int i = 0; i < _stops.Length; i++)
            {
                float t = Normalised(_stops[i]);
                if (t > 0.001f && t < 0.999f)
                {
                    AddTick(i, t);
                }
            }

            SetValue(_current, false, false);
        }

        /// <summary>Set without notifying (a screen reflecting a stored value).</summary>
        public void SetValue(float value, bool animate)
        {
            SetValue(value, animate, false);
        }

        private void SetValue(float value, bool animate, bool notify)
        {
            float v = Quantise(Mathf.Clamp(value, _min, _max));
            bool changed = !Mathf.Approximately(v, _current) || !_built;
            _current = v;
            _value.text = Format(v);

            float t = Normalised(v);
            float x = t * Mathf.Max(1f, RailWidthOrLayout());
            if (animate)
            {
                UiMotion.Move(_bead, new Vector2(x, 0f), UiDuration.Fast, UiEase.Move, UiMotionClass.Decor);
                UiMotion.Resize(_fill, new Vector2(x, _fillHeight), UiDuration.Fast, UiEase.Move, UiMotionClass.Decor);
            }
            else
            {
                UiMotion.KillTarget(_bead, false);
                UiMotion.KillTarget(_fill, false);
                _bead.anchoredPosition = new Vector2(x, 0f);
                _fill.sizeDelta = new Vector2(x, _fillHeight);
            }

            if (notify && changed)
            {
                float now = Time.unscaledTime;
                if (_lastRaise < 0f || now - _lastRaise >= 1f / MaxEventsPerSecond)
                {
                    _lastRaise = now;
                    UiEvents.Raise(UiEvent.SliderChanged, name, t);   // WP-068 s11: the tick's pitch follows the value
                }

                if (_changed != null)
                {
                    _changed(v);
                }
            }
        }

        /// <summary>One keyboard / pad step (the step size, or 1/20 of the range when continuous).</summary>
        public void Step(int direction)
        {
            float next;
            if (_stops != null)
            {
                int index = NearestStop(_current);
                next = _stops[Mathf.Clamp(index + direction, 0, _stops.Length - 1)];
            }
            else
            {
                float size = _step > 0f ? _step : (_max - _min) / 20f;
                next = Mathf.Clamp(_current + direction * size, _min, _max);
            }

            if (Mathf.Approximately(next, _current))
            {
                UiSound.Play(UiSoundKind.Denied);
                return;
            }

            SetValue(next, true, true);
        }

        public override void OnMove(AxisEventData eventData)
        {
            if (eventData.moveDir == MoveDirection.Left || eventData.moveDir == MoveDirection.Right)
            {
                if (IsInteractable())
                {
                    Step(eventData.moveDir == MoveDirection.Right ? 1 : -1);
                }

                eventData.Use();
                return;
            }

            base.OnMove(eventData); // ↑ / ↓ leave the row
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);
            if (IsInteractable() && eventData.button == PointerEventData.InputButton.Left)
            {
                UiCursor.SetPressed(true);   // 12b (AD G1 SHOULD-4): the V1 Press art while the bead is held, as on UiButton
                DragTo(eventData);
            }
        }

        public override void OnPointerUp(PointerEventData eventData)
        {
            UiCursor.SetPressed(false);
            base.OnPointerUp(eventData);
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            eventData.useDragThreshold = false;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (IsInteractable() && eventData.button == PointerEventData.InputButton.Left)
            {
                DragTo(eventData);
            }
        }

        /// <summary>1:1 - the bead goes where the pointer is (no easing while dragging).</summary>
        private void DragTo(PointerEventData eventData)
        {
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rail, eventData.position, eventData.pressEventCamera, out local))
            {
                return;
            }

            float t = Mathf.Clamp01((local.x - _rail.rect.xMin) / Mathf.Max(1f, _rail.rect.width));
            SetValue(Mathf.Lerp(_min, _max, t), false, true);
        }

        /// <summary>Tests: set from a 0..1 rail position as a drag would.</summary>
        public void DragToNormalisedForTests(float t)
        {
            SetValue(_min + (_max - _min) * Mathf.Clamp01(t), false, true);
        }

        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            SetFocusVisible(true);
            if (eventData is AxisEventData)
            {
                UiSound.Play(UiSoundKind.Move);
            }

            UiFocusBrackets.Follow((RectTransform)transform);
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            base.OnDeselect(eventData);
            SetFocusVisible(false);
            UiFocusBrackets.Release((RectTransform)transform);
        }

        protected override void OnDisable()
        {
            UiCursor.SetPressed(false);   // a row that closes mid-drag never leaves the Press art up
            base.OnDisable();
            UiFocusBrackets.Release((RectTransform)transform);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (_built)
            {
                // The rail width is known only after layout: re-place the bead for the current value.
                UiMotion.KillTarget(_bead, false);
                UiMotion.KillTarget(_fill, false);
                float x = Normalised(_current) * Mathf.Max(1f, RailWidthOrLayout());
                _bead.anchoredPosition = new Vector2(x, 0f);
                _fill.sizeDelta = new Vector2(x, _fillHeight);
            }
        }

        private void SetFocusVisible(bool visible)
        {
            if (_ring != null && _ring.activeSelf != visible)
            {
                _ring.SetActive(visible);
                _ringHalo.SetActive(visible);
            }
        }

        private float Normalised(float v)
        {
            return Mathf.Clamp01((v - _min) / (_max - _min));
        }

        private int NearestStop(float v)
        {
            int best = 0;
            for (int i = 1; i < _stops.Length; i++)
            {
                if (Mathf.Abs(_stops[i] - v) < Mathf.Abs(_stops[best] - v))
                {
                    best = i;
                }
            }

            return best;
        }

        private float Quantise(float v)
        {
            if (_stops != null)
            {
                return _stops[NearestStop(v)];
            }

            if (_step <= 0f)
            {
                return v;
            }

            return Mathf.Clamp(_min + Mathf.Round((v - _min) / _step) * _step, _min, _max);
        }

        /// <summary>The rail's width - from layout when it has one, from the row's own numbers before the first layout.</summary>
        private float RailWidthOrLayout()
        {
            float w = _rail.rect.width;
            if (w > 1f)
            {
                return w;
            }

            var parentRect = (RectTransform)_rail.parent;
            return parentRect.sizeDelta.x + _rail.offsetMax.x - _rail.offsetMin.x;
        }
    }
}
