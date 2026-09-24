using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// A value bar: health, reload, Special Weapon timer, profile XP.
    ///
    /// The value is eased rather than snapped, because a health bar that jumps is a
    /// health bar the player cannot read the size of the hit from. Easing runs on
    /// unscaled time so the bar still settles while the round-end freeze or the pause
    /// screen is holding the simulation.
    ///
    /// WP-068 slice 5 · component #16 UiBar v3 (direction §4), all opt-in so the v2 bars
    /// keep their look until their screen slice moves them:
    ///  · <see cref="EnableSegments"/> - 10 hairline ticks (1 px ink) + one strong tick at 30 %;
    ///  · <see cref="EnableGhost"/> - the lost-value ghost (Cream300 at 0.5 α): on a drop it
    ///    holds the old value 350 ms, then falls to the new one (BarGhost preset, Delta);
    ///  · <see cref="SetRegen"/> - a 4 px grey track 2 px BELOW the bar: time until regen
    ///    (1 → 0, drains right to left; Data). Never the health colour - it is a time, not
    ///    health (AD 5b MUST-4).
    /// Low state stays the screen's job: glyph + word, never the colour alone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiBar : MonoBehaviour
    {
        private Image _fill;
        private UiColor _colorToken;
        private float _target = 1f;
        private UiTweenHandle _tween;
        private Image _ghost;
        private Image _regen;

        public const float GhostAlpha = 0.5f;
        public const float StrongTickAt = 0.3f;
        public const float RegenHeight = 4f;
        public const float RegenGap = 2f;
        public const float RegenAlpha = 0.6f;

        /// <summary>The ghost's current fill (tests); -1 without a ghost.</summary>
        public float GhostValue
        {
            get { return _ghost != null ? _ghost.fillAmount : -1f; }
        }

        /// <summary>Add the lost-value ghost behind the fill (Cream300, 0.5 α).</summary>
        public void EnableGhost()
        {
            if (_ghost != null || _fill == null)
            {
                return;
            }

            _ghost = UiFactory.NewImage("Ghost", _fill.transform.parent, UiColor.TextSecondary, _fill.sprite);
            CopyRect((RectTransform)_fill.transform, (RectTransform)_ghost.transform);
            _ghost.type = Image.Type.Filled;
            _ghost.fillMethod = Image.FillMethod.Horizontal;
            _ghost.fillOrigin = (int)Image.OriginHorizontal.Left;
            _ghost.fillAmount = _fill.fillAmount;
            Color c = _ghost.color;
            c.a = GhostAlpha;
            _ghost.color = c;
            _ghost.transform.SetSiblingIndex(_fill.transform.GetSiblingIndex());
        }

        /// <summary>Add <paramref name="count"/> hairline ticks over the fill and one 2 px tick at 30 %.</summary>
        public void EnableSegments(int count = 10)
        {
            if (_fill == null)
            {
                return;
            }

            Transform host = _fill.transform.parent;
            for (int i = 1; i < count; i++)
            {
                AddTick(host, (float)i / count, 1f);
            }

            // The low-health mark: 2 px TextSecondary 0.6 so it reads against the ink hairlines (AD 5b SHOULD-2).
            Image strong = AddTick(host, StrongTickAt, 2f);
            strong.color = UiTokens.Get(UiColor.TextSecondary);
            Color sc = strong.color;
            sc.a = 0.6f;
            strong.color = sc;
        }

        /// <summary>Time until regen as a 0..1 fraction (1 = just hit, 0 = regen starts): a 4 px TextSecondary 0.6 track 2 px below the bar (Data: set as-is). Negative hides it.</summary>
        public void SetRegen(float value01)
        {
            if (value01 < 0f)
            {
                if (_regen != null && _regen.gameObject.activeSelf)
                {
                    _regen.gameObject.SetActive(false);
                }

                return;
            }

            if (_regen == null)
            {
                _regen = UiFactory.NewImage("Regen", _fill.transform.parent, UiColor.TextSecondary, UiSprites.Solid);
                Color c = _regen.color;
                c.a = RegenAlpha;
                _regen.color = c;
                var rect = (RectTransform)_regen.transform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, RegenHeight);
                rect.anchoredPosition = new Vector2(0f, -RegenGap);
                _regen.type = Image.Type.Filled;
                _regen.fillMethod = Image.FillMethod.Horizontal;
                _regen.fillOrigin = (int)Image.OriginHorizontal.Left;
            }

            if (!_regen.gameObject.activeSelf)
            {
                _regen.gameObject.SetActive(true);
            }

            float v = Mathf.Clamp01(value01);
            if (Mathf.Abs(_regen.fillAmount - v) >= 1f / 256f)
            {
                _regen.fillAmount = v;
            }
        }

        private Image AddTick(Transform host, float at, float width)
        {
            Image tick = UiFactory.NewImage(width > 1f ? "StrongTick" : "Tick", host, UiColor.BarTrack, UiSprites.Solid);
            var rect = (RectTransform)tick.transform;
            rect.anchorMin = new Vector2(at, 0f);
            rect.anchorMax = new Vector2(at, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, 0f);
            rect.anchoredPosition = Vector2.zero;
            return tick;
        }

        private static void CopyRect(RectTransform from, RectTransform to)
        {
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.offsetMin = from.offsetMin;
            to.offsetMax = from.offsetMax;
        }

        public void Configure(Image fill, UiColor colorToken)
        {
            _fill = fill;
            _colorToken = colorToken;
            _target = fill != null ? fill.fillAmount : 1f;
        }

        /// <summary>Sets the 0..1 value. Eased unless <paramref name="instant"/>.</summary>
        public void SetValue(float value01, bool instant = false)
        {
            float target = Mathf.Clamp01(value01);
            bool changed = !Mathf.Approximately(target, _target);
            float previous = _target;
            _target = target;
            if (_fill == null)
            {
                return;
            }

            if (_ghost != null && changed)
            {
                if (target < previous && !instant)
                {
                    UiPresets.BarGhost(_ghost, Mathf.Max(previous, _ghost.fillAmount), target);
                }
                else
                {
                    UiMotion.KillTarget(_ghost, false);
                    _ghost.fillAmount = target; // a gain never leaves a ghost
                }
            }

            if (instant)
            {
                UiMotion.KillTarget(_fill, false);
                _fill.fillAmount = _target;
            }
            else if (changed || (!_tween.IsAlive && !Mathf.Approximately(_fill.fillAmount, _target)))
            {
                // WP-068 slice 2: one Fill tween on the UiMotion core instead of a
                // never-ending per-frame lerp. Same 220 ms (Base, Settle). Data class: the
                // bar shows a game value, so Reduced Motion never changes how it moves.
                _tween = UiMotion.Fill(_fill, _target, UiDuration.Base, UiEase.Settle, UiMotionClass.Data);
            }
        }

        /// <summary>Swaps the fill token, e.g. health dropping into the low-health colour.</summary>
        public void SetColor(UiColor token)
        {
            if (_colorToken == token || _fill == null)
            {
                return;
            }

            _colorToken = token;
            _fill.color = UiTokens.Get(token);
        }

        public float Value
        {
            get { return _target; }
        }

    }

    /// <summary>
    /// Focus indicator for a <see cref="Slider"/>.
    ///
    /// O4 asks for a visible focus indicator on 100% of interactive elements, and the
    /// pause screen's Camera Shake Intensity slider is one of them. Selectable's own
    /// transitions cannot draw a ring, so this watches the EventSystem instead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiSliderFocus : MonoBehaviour
    {
        private Slider _slider;
        private GameObject _halo;
        private GameObject _ring;

        public void Configure(Slider slider, GameObject halo, GameObject ring)
        {
            _slider = slider;
            _halo = halo;
            _ring = ring;

            SetVisible(false);
        }

        private void Update()
        {
            if (_slider == null)
            {
                return;
            }

            bool focused = _slider.IsInteractable()
                           && EventSystem.current != null
                           && EventSystem.current.currentSelectedGameObject == _slider.gameObject;

            SetVisible(focused);
        }

        private void SetVisible(bool visible)
        {
            if (_halo != null && _halo.activeSelf != visible)
            {
                _halo.SetActive(visible);
            }

            if (_ring != null && _ring.activeSelf != visible)
            {
                _ring.SetActive(visible);
            }
        }
    }
}
