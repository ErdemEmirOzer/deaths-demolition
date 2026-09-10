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
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiBar : MonoBehaviour
    {
        private Image _fill;
        private UiColor _colorToken;
        private float _target = 1f;
        private bool _instant;

        public void Configure(Image fill, UiColor colorToken)
        {
            _fill = fill;
            _colorToken = colorToken;
            _target = fill != null ? fill.fillAmount : 1f;
        }

        /// <summary>Sets the 0..1 value. Eased unless <paramref name="instant"/>.</summary>
        public void SetValue(float value01, bool instant = false)
        {
            _target = Mathf.Clamp01(value01);

            if (instant && _fill != null)
            {
                _fill.fillAmount = _target;
            }

            _instant = instant;
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

        private void Update()
        {
            if (_fill == null || _instant)
            {
                _instant = false;
                return;
            }

            float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.0001f, UiTokens.MotionBase));
            _fill.fillAmount = Mathf.Lerp(_fill.fillAmount, _target, t);
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
