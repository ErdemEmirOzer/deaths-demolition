using UnityEngine;
using UnityEngine.EventSystems;
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
        // WP-068 slice 2: a thin shim over the UiMotion core (Attention class) - same
        // alpha range and period as before, now on CanvasRenderer alpha (every pulse target
        // has base alpha 1, so the composite is identical) and without an Update of its own.
        // Reduced Motion holds at the top of the swing (the core's Attention policy).
        // Enabling / disabling the component starts / stops the pulse (the per-component
        // allocation diagnostics toggle it).
        private Graphic[] _targets;
        private readonly System.Collections.Generic.List<UiTweenHandle> _handles = new System.Collections.Generic.List<UiTweenHandle>(2);
        private float _minAlpha = 0.55f;
        private float _maxAlpha = 1f;
        private float _period = UiTokens.PulsePeriod;

        public void Configure(Graphic[] targets, float minAlpha, float maxAlpha, float period)
        {
            _targets = targets;
            _minAlpha = Mathf.Clamp01(minAlpha);
            _maxAlpha = Mathf.Clamp01(maxAlpha);
            _period = Mathf.Max(0.1f, period);
            if (isActiveAndEnabled)
            {
                Restart();
            }
        }

        public void Configure(Graphic target)
        {
            Configure(new[] { target }, 0.55f, 1f, UiTokens.PulsePeriod);
        }

        private void OnEnable()
        {
            Restart();
        }

        private void OnDisable()
        {
            Stop();
        }

        private void Restart()
        {
            UiSettings.EnsureLoaded(); // the RM mirror the core reads
            Stop();
            if (_targets == null)
            {
                return;
            }

            for (int i = 0; i < _targets.Length; i++)
            {
                Graphic graphic = _targets[i];
                if (graphic == null)
                {
                    continue;
                }

                graphic.canvasRenderer.SetAlpha(_maxAlpha);
                _handles.Add(UiMotion.Pulse(graphic, _minAlpha, _maxAlpha, _period, GetInstanceID()));
            }
        }

        private void Stop()
        {
            for (int i = 0; i < _handles.Count; i++)
            {
                UiMotion.Kill(_handles[i], false);
            }

            _handles.Clear();
        }
    }

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
    /// The one easing this module uses (WP-022C motion standard, interaction-designer):
    /// ease-out (fast start, soft landing) for everything that appears or settles -
    /// screen fades, focus/hover washes (UiButton's exponential lerp is the same family);
    /// the attention pulse is a cosine wave (UiPulse). Durations: UiTokens.Motion* (0.08 /
    /// 0.14 / 0.22 / 0.28 s), all unscaled; reduced motion collapses every one to instant.
    /// </summary>
    public static class UiMotionCurve
    {
        public static float EaseOut(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t);
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
        // WP-068 slice 2: shim over the UiMotion core - the screen's CanvasGroup fades in
        // over MotionScreen (280 ms, Settle; Decor class, so Reduced Motion lands it at once).
        public void Configure(CanvasGroup group)
        {
            UiSettings.EnsureLoaded(); // the RM mirror the core reads
            if (group == null)
            {
                Destroy(this);
                return;
            }

            group.alpha = 0f;
            // WP-068 slice 10: a screen that opens behind the loading curtain starts its
            // intro when the curtain reveals it, not unseen at Start().
            if (isActiveAndEnabled)
            {
                StartCoroutine(Play(group));
            }
            else
            {
                UiMotion.Alpha(group, 1f, UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor);
                Destroy(this);
            }
        }

        private System.Collections.IEnumerator Play(CanvasGroup group)
        {
            yield return UiCurtain.WaitRevealed();
            if (group != null)
            {
                UiMotion.Alpha(group, 1f, UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor);
            }

            Destroy(this);
        }
    }

    [DisallowMultipleComponent]
    public sealed class UiStaggerIn : MonoBehaviour
    {
        private const float Lift = 12f;

        // WP-068 slice 2: shim over the UiMotion core - each group rises 12 px and fades in
        // over MotionBase (220 ms, Settle), one step apart (Decor: Reduced Motion lands them).
        public void Configure(CanvasGroup[] groups, float step = 0.06f)
        {
            UiSettings.EnsureLoaded(); // the RM mirror the core reads
            if (groups != null)
            {
                for (int i = 0; i < groups.Length; i++)
                {
                    if (groups[i] != null)
                    {
                        groups[i].alpha = 0f;
                    }
                }

                // WP-068 slice 10: start once the loading curtain has revealed the screen.
                if (isActiveAndEnabled)
                {
                    StartCoroutine(Play(groups, step));
                    return;
                }

                StartNow(groups, step);
            }

            Destroy(this);
        }

        private System.Collections.IEnumerator Play(CanvasGroup[] groups, float step)
        {
            yield return UiCurtain.WaitRevealed();
            StartNow(groups, step);
            Destroy(this);
        }

        private static void StartNow(CanvasGroup[] groups, float step)
        {
            if (groups != null)
            {
                for (int i = 0; i < groups.Length; i++)
                {
                    CanvasGroup group = groups[i];
                    if (group == null)
                    {
                        continue;
                    }

                    var rect = (RectTransform)group.transform;
                    Vector2 rest = rect.anchoredPosition;
                    group.alpha = 0f;
                    rect.anchoredPosition = rest - new Vector2(0f, Lift);
                    float delay = i * step;
                    UiMotion.Alpha(group, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor, delay);
                    UiMotion.Move(rect, rest, UiDuration.Base, UiEase.Settle, UiMotionClass.Decor, delay);
                }
            }
        }
    }

    [DisallowMultipleComponent]
    public sealed class UiMaxWidth : UIBehaviour
    {
        private float _max = UiTokens.ContentMaxWidth;
        private bool _applying;

        public void Configure(float maxWidth)
        {
            _max = maxWidth;
            Apply();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();

            // Re-entrancy guard. Each offset write below fires this callback again
            // before the write is complete; without the guard the inset was re-derived
            // from a half-applied rect and the Player overflowed its stack at 2560 px
            // (the only width where the cap actually engages - crash reports in
            // wp-022/evidence/crash-reports/).
            if (!_applying)
            {
                Apply();
            }
        }

        private void Apply()
        {
            var rect = transform as RectTransform;
            var parent = transform.parent as RectTransform;
            if (rect == null || parent == null || _applying)
            {
                return;
            }

            float available = parent.rect.width - (int)UiSpace.Page * 2;
            float width = Mathf.Min(available, _max);
            float inset = Mathf.Round(Mathf.Max(0f, (parent.rect.width - width) * 0.5f));

            // Stretch-anchored on x: the horizontal inset is one sizeDelta write with the
            // anchored position centred. One write, one callback, already guarded.
            Vector2 size = rect.sizeDelta;
            Vector2 pos = rect.anchoredPosition;
            if (Mathf.Approximately(size.x, -2f * inset) && Mathf.Approximately(pos.x, 0f))
            {
                return;
            }

            _applying = true;
            try
            {
                rect.sizeDelta = new Vector2(-2f * inset, size.y);
                rect.anchoredPosition = new Vector2(0f, pos.y);
            }
            finally
            {
                _applying = false;
            }
        }
    }
}
