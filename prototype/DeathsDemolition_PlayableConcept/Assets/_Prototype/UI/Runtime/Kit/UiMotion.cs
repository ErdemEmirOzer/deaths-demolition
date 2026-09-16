using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// Attention pulse for a graphic.
    ///
    /// Used by exactly two things - the Special Weapon final-seconds warning and a
    /// newly unlocked entry on the results screen - because a pulse that appears in a
    /// third place stops meaning "look here".
    ///
    /// It moves alpha between a floor and a ceiling instead of switching a graphic on
    /// and off: a hard blink is both harder to read and worse for photosensitivity, and
    /// the element never disappears, so the information it carries is never absent.
    /// Period is <see cref="UiTokens.PulsePeriod"/>, i.e. well under 3 Hz.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiPulse : MonoBehaviour
    {
        private Graphic[] _targets;
        private bool _held;
        private float _minAlpha = 0.55f;
        private float _maxAlpha = 1f;
        private float _period = UiTokens.PulsePeriod;

        public void Configure(Graphic[] targets, float minAlpha, float maxAlpha, float period)
        {
            _targets = targets;
            _minAlpha = Mathf.Clamp01(minAlpha);
            _maxAlpha = Mathf.Clamp01(maxAlpha);
            _period = Mathf.Max(0.1f, period);
        }

        public void Configure(Graphic target)
        {
            Configure(new[] { target }, 0.55f, 1f, UiTokens.PulsePeriod);
        }

        private void Update()
        {
            if (_targets == null)
            {
                return;
            }

            // Unscaled: the warning has to keep breathing while the world is frozen.
            // Reduced motion (WP-022B): the pulse holds at full alpha - the element stays
            // visible, it just stops breathing.
            float wave = UiSettings.ReducedMotion
                ? 1f
                : 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * (2f * Mathf.PI / _period));
            float alpha = Mathf.Lerp(_minAlpha, _maxAlpha, wave);
            if (UiSettings.ReducedMotion && _held)
            {
                return;
            }

            _held = UiSettings.ReducedMotion;

            for (int i = 0; i < _targets.Length; i++)
            {
                Graphic graphic = _targets[i];
                if (graphic == null)
                {
                    continue;
                }

                Color c = graphic.color;
                c.a = alpha;
                graphic.color = c;
            }
        }
    }

    /// <summary>
    /// A single drifting sakura petal on the menu backdrop.
    ///
    /// Six of these is the entire "atmosphere" budget of the menu. The request asks for
    /// controlled theme detail, and the control is the count: petals are large, slow,
    /// far below text contrast and behind every panel, so they cannot compete with the
    /// thing the player is trying to read.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiDrift : MonoBehaviour
    {
        private RectTransform _rect;
        private Vector2 _velocity;
        private float _spin;
        private float _resetAbove;
        private float _spanX;

        public void Configure(RectTransform rect, Vector2 velocity, float spin, float spanX, float resetAbove)
        {
            _rect = rect;
            _velocity = velocity;
            _spin = spin;
            _spanX = spanX;
            _resetAbove = resetAbove;
        }

        private void Update()
        {
            if (_rect == null)
            {
                return;
            }

            if (UiSettings.ReducedMotion)
            {
                return; // petals stand still
            }

            float dt = Time.unscaledDeltaTime;
            _rect.anchoredPosition += _velocity * dt;
            _rect.localRotation = Quaternion.Euler(0f, 0f, _rect.localEulerAngles.z + _spin * dt);

            if (_rect.anchoredPosition.y < -_resetAbove)
            {
                _rect.anchoredPosition = new Vector2(
                    Random.Range(-_spanX, _spanX),
                    _resetAbove);
            }
        }
    }

    /// <summary>
    /// Screen entrance (WP-022B micro-animation): the canvas fades from 0 to 1 over
    /// UiTokens.MotionScreen on unscaled time, then removes itself. Instant when reduced
    /// motion is on. One CanvasGroup alpha per frame - no layout, no vertex rebuild.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiScreenFade : MonoBehaviour
    {
        private CanvasGroup _group;
        private float _start;

        public void Configure(CanvasGroup group)
        {
            _group = group;
            _start = Time.unscaledTime;
            if (_group != null)
            {
                _group.alpha = UiSettings.ReducedMotion ? 1f : 0f;
            }
        }

        private void Update()
        {
            if (_group == null)
            {
                Destroy(this);
                return;
            }

            float t = UiSettings.ReducedMotion
                ? 1f
                : Mathf.Clamp01((Time.unscaledTime - _start) / UiTokens.MotionScreen);
            _group.alpha = t;
            if (t >= 1f)
            {
                Destroy(this);
            }
        }
    }
}
