using System.Collections.Generic;
using TMPro;
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
        private Graphic _graphic;
        private float _alpha;
        private float _shown = -1f;
        private UiTextKeepOut _keepOut;

        public void Configure(RectTransform rect, Vector2 velocity, float spin, float spanX, float resetAbove)
        {
            _rect = rect;
            _velocity = velocity;
            _spin = spin;
            _spanX = spanX;
            _resetAbove = resetAbove;
        }

        /// <summary>AD 12b SHOULD-5: the graphic fades out wherever it nears a word (its alpha now is its full alpha).</summary>
        public void KeepOut(Graphic graphic, UiTextKeepOut keepOut)
        {
            _graphic = graphic;
            _alpha = graphic != null ? graphic.color.a : 0f;
            _keepOut = keepOut;
            _shown = -1f;
        }

        /// <summary>The share of its full alpha the graphic shows now (1 without a keep-out).</summary>
        public float Visibility
        {
            get { return _keepOut == null || _alpha <= 0f ? 1f : Mathf.Clamp01(_shown / _alpha); }
        }

        private void Update()
        {
            if (_rect == null)
            {
                return;
            }

            // Reduced Motion: petals stand still - but a still petal keeps out of the words all the same.
            if (!UiSettings.ReducedMotion)
            {
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

            ApplyKeepOut();
        }

        /// <summary>Sets the alpha for where the graphic is now (every frame from Update; tests call it directly).</summary>
        public void ApplyKeepOut()
        {
            if (_keepOut == null || _graphic == null)
            {
                return;
            }

            // The petal spins, so its whole square (not the silhouette) is what must stay clear.
            Vector2 size = _rect.rect.size;
            Vector3 at = _rect.localPosition;
            var box = new Rect(at.x - size.x * 0.5f, at.y - size.y * 0.5f, size.x, size.y);
            float alpha = _alpha * _keepOut.Visibility(box);
            if (_shown >= 0f && Mathf.Abs(alpha - _shown) < 0.002f)
            {
                return; // no vertex rebuild for a change nobody sees
            }

            _shown = alpha;
            Color c = _graphic.color;
            c.a = alpha;
            _graphic.color = c;
        }
    }

    /// <summary>
    /// AD 12b SHOULD-5 (the 6ab MUST-3 rule carried to the paper screens): the backdrop's petals keep out of the
    /// words. The wallpaper's clamp leaves them no light to spend - on its brightest texel (L 0.0245) TextMuted is
    /// already at 4.7:1, and even a 0.02 sakura petal over it drops that to 4.2:1 - so instead of a dimmer petal, no
    /// petal lies under a letter: every live TMP_Text on the screen gives its glyph bounds (padded by
    /// <see cref="Pad"/>) in the backdrop's space, gathered at most every <see cref="RefreshSeconds"/>, and a petal's
    /// alpha falls to 0 over <see cref="Feather"/> px as its square nears one. Words on an opaque panel count too;
    /// the petal is under the panel there, so hiding it costs nothing.
    /// </summary>
    public sealed class UiTextKeepOut
    {
        public const float Pad = 8f;
        public const float Feather = 40f;
        public const float RefreshSeconds = 0.25f;

        private static readonly Vector3[] Corners = new Vector3[4];

        private readonly RectTransform _source;
        private readonly RectTransform _space;
        private readonly List<TMP_Text> _texts = new List<TMP_Text>(64);
        private Rect[] _rects = new Rect[64];
        private int _count;
        private int _refreshFrame = -1;
        private float _nextRefresh = float.NegativeInfinity;

        /// <param name="source">The root whose words count (the screen).</param>
        /// <param name="space">The petals' parent: the rects are in its local space.</param>
        public UiTextKeepOut(RectTransform source, RectTransform space)
        {
            _source = source;
            _space = space;
        }

        /// <summary>The words gathered at the last refresh (tests, capture logs).</summary>
        public int Count
        {
            get { return _count; }
        }

        public Rect RectAt(int index)
        {
            return _rects[index];
        }

        /// <summary>The share of a petal's alpha it may show with its square at <paramref name="petal"/> (0 over a word).</summary>
        public float Visibility(Rect petal)
        {
            RefreshIfDue();
            return VisibilityAmong(petal, _rects, _count, Feather);
        }

        /// <summary>Forces the next <see cref="Visibility"/> to re-gather the words (a screen that just reflowed).</summary>
        public void Invalidate()
        {
            _nextRefresh = float.NegativeInfinity;
            _refreshFrame = -1;
        }

        public static float VisibilityAmong(Rect petal, Rect[] rects, int count, float feather)
        {
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                float gap = Gap(petal, rects[i]);
                if (gap <= 0f)
                {
                    return 0f;
                }

                if (gap < nearest)
                {
                    nearest = gap;
                }
            }

            return feather <= 0f ? 1f : Mathf.Clamp01(nearest / feather);
        }

        /// <summary>The distance between two rects, 0 when they touch or overlap.</summary>
        public static float Gap(Rect a, Rect b)
        {
            float dx = Mathf.Max(0f, Mathf.Max(b.xMin - a.xMax, a.xMin - b.xMax));
            float dy = Mathf.Max(0f, Mathf.Max(b.yMin - a.yMax, a.yMin - b.yMax));
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private void RefreshIfDue()
        {
            float now = Time.unscaledTime;
            if (_refreshFrame == Time.frameCount || now < _nextRefresh)
            {
                return;
            }

            _refreshFrame = Time.frameCount;
            _nextRefresh = now + RefreshSeconds;
            _count = 0;
            if (_source == null || _space == null)
            {
                return;
            }

            _source.GetComponentsInChildren(false, _texts);
            for (int i = 0; i < _texts.Count; i++)
            {
                TMP_Text text = _texts[i];
                if (!text.enabled || text.color.a <= 0.01f)
                {
                    continue;
                }

                Bounds bounds = text.textBounds; // the glyphs, not the box: a wide label box is not a word
                if (bounds.size.x <= 0f || bounds.size.y <= 0f)
                {
                    continue;
                }

                Transform t = text.transform;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                Corners[0] = _space.InverseTransformPoint(t.TransformPoint(new Vector3(min.x, min.y, 0f)));
                Corners[1] = _space.InverseTransformPoint(t.TransformPoint(new Vector3(min.x, max.y, 0f)));
                Corners[2] = _space.InverseTransformPoint(t.TransformPoint(new Vector3(max.x, max.y, 0f)));
                Corners[3] = _space.InverseTransformPoint(t.TransformPoint(new Vector3(max.x, min.y, 0f)));
                float xMin = Mathf.Min(Mathf.Min(Corners[0].x, Corners[1].x), Mathf.Min(Corners[2].x, Corners[3].x));
                float xMax = Mathf.Max(Mathf.Max(Corners[0].x, Corners[1].x), Mathf.Max(Corners[2].x, Corners[3].x));
                float yMin = Mathf.Min(Mathf.Min(Corners[0].y, Corners[1].y), Mathf.Min(Corners[2].y, Corners[3].y));
                float yMax = Mathf.Max(Mathf.Max(Corners[0].y, Corners[1].y), Mathf.Max(Corners[2].y, Corners[3].y));

                if (_count == _rects.Length)
                {
                    System.Array.Resize(ref _rects, _rects.Length * 2);
                }

                _rects[_count++] = Rect.MinMaxRect(xMin - Pad, yMin - Pad, xMax + Pad, yMax + Pad);
            }

            _texts.Clear();
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

            // AD 10b-2 MUST-2: under the ink the recede is the reveal - the screen is on screen before it.
            if (UiCurtain.InkCovers)
            {
                group.alpha = 1f;
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
            if (UiCurtain.InkCovers)
            {
                // AD 10b-2 MUST-2: under the ink the columns are already at rest when the recede uncovers them.
                Destroy(this);
                return;
            }

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
