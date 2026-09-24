using DD.Prototype.Enemies;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-022I · enemy status markers for the WP-055 Special reactions: a small word chip
    /// ("NET" for a netted / rooted body, "DAZED" for a Kagura daze) floating over each
    /// affected zombie's head. The marker exists so the player can read WHY a body has
    /// stopped or slowed without watching its animation - shape (chip) + word, never a
    /// colour alone (directive §14).
    ///
    /// WP-068 slice 8c · E19 status stamps, in the 8-R ink language (AD 8-R MUST-3):
    /// - Each stamp is the 24 px outer-only silhouette (G1 Status.NettedOuter / DazedOuter, SDF ink edge) plus the word in
    ///   cream with the HUD ink, over a brush underline - no box. A status is not a threat, so there is no state colour:
    ///   shape and word tell NET from DAZED.
    /// - A chip is bound to its body for as long as the status lasts. It stamps in (1.12 → 1) when the status starts and
    ///   fades out (Fast, Exit) where it last stood when the status ends, the body dies or leaves the view.
    /// - Positions follow the projection with no lag.
    /// - The status timers are private to the enemy module, so there is no remaining-time underline: a duration is
    ///   never invented (E19).
    ///
    /// Zero allocation per frame (ADR-037-1): a fixed pool of chips built once, the
    /// live-zombie list is indexed (no enumerator), texts are pre-built literals swapped
    /// only when a marker's state changes, and an unused chip is parked off-screen rather
    /// than SetActive-toggled (the WP-022 reticle lesson - no canvas rebuild), at group alpha 0
    /// so it is not drawn either (HUD budget: a group at alpha 0 costs no draw, PilotPerfProbe 04h).
    /// </summary>
    public sealed class UiEnemyStatusMarkers : MonoBehaviour
    {
        public const int PoolSize = 16;
        public const float GlyphSize = 24f;
        private const float HeadHeight = 2.1f;
        private const float MinChipWidth = 88f;
        private static readonly Vector2 Parked = new Vector2(-10000f, -10000f);
        private static readonly Vector3[] s_corners = new Vector3[4];

        private enum State { None = 0, Netted = 1, Dazed = 2 }

        private RectTransform[] _chips;
        private CanvasGroup[] _groups;
        private Image[] _glyphs;
        private Sprite _nettedSprite;
        private Sprite _dazedSprite;
        private TMP_Text[] _labels;
        private State[] _shown;
        private ZombieEnemy[] _owner;
        private bool[] _owned;
        private float[] _leftAt;
        private RectTransform _canvas;
        private float _exitSeconds;

        /// <summary>Markers bound to a body in the last update (test readout; a fading chip no longer counts).</summary>
        public int VisibleCount { get; private set; }

        public void Configure(RectTransform canvasRect)
        {
            _canvas = canvasRect;
            _chips = new RectTransform[PoolSize];
            _groups = new CanvasGroup[PoolSize];
            _glyphs = new Image[PoolSize];
            _nettedSprite = UiArt.Icon("Status.NettedOuter") ?? UiArt.Icon("Status.Netted");
            _dazedSprite = UiArt.Icon("Status.DazedOuter") ?? UiArt.Icon("Status.Dazed");
            bool glyphs = _nettedSprite != null && _dazedSprite != null;
            _labels = new TMP_Text[PoolSize];
            _shown = new State[PoolSize];
            _owner = new ZombieEnemy[PoolSize];
            _owned = new bool[PoolSize];
            _leftAt = new float[PoolSize];
            _exitSeconds = UiMotionTokens.Seconds(UiDuration.Fast);

            // E19: the chip is as tall as the glyph plus a 2 px margin, or the word's line at this TextScale if taller;
            // as wide as glyph + the longer word, never under the old 88.
            float glyph = glyphs ? GlyphSize : 0f;
            float height = Mathf.Max(GlyphSize + 4f, UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Label)));
            Sprite brush = UiFxGlobals.InkMask(UiInkChannel.Underline);
            for (int i = 0; i < PoolSize; i++)
            {
                TMP_Text label = UiFactory.Chip("Status." + i, transform, "DAZED", UiColor.SurfaceInset, UiColor.TextPrimary, MinChipWidth, height);
                label.fontStyle = FontStyles.Bold;
                UiHudInk.Apply(label);
                float words = Mathf.Ceil(label.GetPreferredValues("DAZED").x);
                float width = Mathf.Max(MinChipWidth, (int)UiSpace.XS + glyph + (int)UiSpace.XS + words + (int)UiSpace.S);
                var chip = (RectTransform)label.transform.parent;
                chip.sizeDelta = new Vector2(width, height);
                chip.anchorMin = new Vector2(0.5f, 0.5f);
                chip.anchorMax = new Vector2(0.5f, 0.5f);
                chip.pivot = new Vector2(0.5f, 0f);
                chip.anchoredPosition = Parked;
                _chips[i] = chip;
                Image fill = chip.GetComponent<Image>();
                Color clear = fill.color;
                clear.a = 0f;
                fill.color = clear;   // the chip's rect stays for layout; nothing of it is drawn
                _groups[i] = UiFactory.Group(chip.gameObject);
                _groups[i].alpha = 0f;   // parked and unbound: culled, not drawn off-screen (a bind sets 1)
                _labels[i] = label;
                label.text = "NET";
                if (glyphs)
                {
                    // Word + glyph (never colour alone): the glyph sits in the chip's left inset
                    // and the label's own left inset moves past it.
                    Image image = UiFactory.NewImage("Icon", chip, UiColor.TextPrimary, _nettedSprite);
                    image.preserveAspect = true;
                    if (UiArt.IsGlyph(image.sprite))
                    {
                        UiArt.ApplyGlyph(image, new Vector3(0.25f, 0f, 0f));
                    }

                    var glyphRect = (RectTransform)image.transform;
                    glyphRect.anchorMin = new Vector2(0f, 0.5f);
                    glyphRect.anchorMax = new Vector2(0f, 0.5f);
                    glyphRect.pivot = new Vector2(0f, 0.5f);
                    glyphRect.sizeDelta = new Vector2(GlyphSize, GlyphSize);
                    glyphRect.anchoredPosition = new Vector2((int)UiSpace.XS, 0f);
                    var labelRect = (RectTransform)label.transform;
                    labelRect.offsetMin = new Vector2((int)UiSpace.XS + GlyphSize + (int)UiSpace.XS, 0f);
                    _glyphs[i] = image;
                }

                Image underline = UiFactory.NewImage("Underline", chip, UiColor.TextPrimary, brush != null ? brush : UiSprites.Solid);
                var underlineRect = underline.rectTransform;
                underlineRect.anchorMin = new Vector2(0f, 0f);
                underlineRect.anchorMax = new Vector2(1f, 0f);
                underlineRect.pivot = new Vector2(0.5f, 0.5f);
                underlineRect.offsetMin = new Vector2((int)UiSpace.XS + glyph + (int)UiSpace.XS, 0f);
                underlineRect.offsetMax = new Vector2(-(int)UiSpace.S, 4f);
                Color line = underline.color;
                line.a = 0.8f;
                underline.color = line;
                underline.transform.SetSiblingIndex(0);

                _shown[i] = State.None;
                _leftAt[i] = -1f;
            }
        }

        private void LateUpdate()
        {
            if (_chips == null || _canvas == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            Camera camera = Camera.main;
            Vector2 canvasSize = _canvas.rect.size;

            // 1 · every bound chip follows its body, or lets go of it (fade where it stood).
            int bound = 0;
            for (int i = 0; i < PoolSize; i++)
            {
                if (!_owned[i])
                {
                    if (_leftAt[i] >= 0f && now - _leftAt[i] >= _exitSeconds)
                    {
                        _leftAt[i] = -1f;
                        _shown[i] = State.None;
                        _chips[i].anchoredPosition = Parked;
                    }

                    continue;
                }

                ZombieEnemy z = _owner[i];
                State state = StateOf(z);
                Vector2 at;
                if (state != State.None && Project(camera, z, canvasSize, out at))
                {
                    _chips[i].anchoredPosition = at;
                    ReportAvoid(z, _chips[i]);
                    if (state != _shown[i])
                    {
                        Style(i, state); // NET → DAZED on the same body: a new word stamps
                        UiPresets.StampIn(_chips[i]);
                    }

                    bound++;
                    continue;
                }

                Release(i, now);
            }

            // 2 · a body newly affected (and in view) takes a free chip, which stamps in.
            System.Collections.Generic.IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            if (camera != null && live != null)
            {
                for (int n = 0; n < live.Count && bound < PoolSize; n++)
                {
                    ZombieEnemy z = live[n];
                    State state = StateOf(z);
                    if (state == State.None || Owns(z))
                    {
                        continue;
                    }

                    Vector2 at;
                    if (!Project(camera, z, canvasSize, out at))
                    {
                        continue;
                    }

                    int slot = FreeSlot();
                    if (slot < 0)
                    {
                        break;
                    }

                    _owner[slot] = z;
                    _owned[slot] = true;
                    _leftAt[slot] = -1f;
                    _chips[slot].anchoredPosition = at;
                    ReportAvoid(z, _chips[slot]);
                    Style(slot, state);
                    UiMotion.KillTarget(_groups[slot], false);
                    _groups[slot].alpha = 1f;
                    UiPresets.StampIn(_chips[slot]);
                    bound++;
                }
            }

            VisibleCount = bound;
        }

        /// <summary>
        /// 8-R · A SHOULD-2 / AD 8c SHOULD-6 (S-07 E18b contract): a bound stamp tells the damage numbers where it stands, every
        /// frame, so a number on the same body rises above it. Screen px, bottom-left (the overlay canvas's world corners);
        /// target = the body's instance id; a report not renewed lapses after one frame. No allocation.
        /// </summary>
        private static void ReportAvoid(ZombieEnemy z, RectTransform chip)
        {
            chip.GetWorldCorners(s_corners);
            UiWorldNumbers.ReportAvoidRect(z.GetInstanceID(),
                Rect.MinMaxRect(s_corners[0].x, s_corners[0].y, s_corners[2].x, s_corners[2].y));
        }

        private static State StateOf(ZombieEnemy z)
        {
            if (z == null || !z.IsAlive)
            {
                return State.None;
            }

            return z.IsNetted ? State.Netted : z.IsDazed ? State.Dazed : State.None;
        }

        private static bool Project(Camera camera, ZombieEnemy z, Vector2 canvasSize, out Vector2 at)
        {
            at = Parked;
            if (camera == null)
            {
                return false;
            }

            Vector3 viewport = camera.WorldToViewportPoint(z.transform.position + Vector3.up * HeadHeight);
            if (viewport.z < 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
            {
                return false;
            }

            at = new Vector2((viewport.x - 0.5f) * canvasSize.x, (viewport.y - 0.5f) * canvasSize.y);
            return true;
        }

        private bool Owns(ZombieEnemy z)
        {
            for (int i = 0; i < PoolSize; i++)
            {
                if (_owned[i] && ReferenceEquals(_owner[i], z))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A chip neither bound nor fading; else the one fading longest (a new stamp beats an old goodbye).</summary>
        private int FreeSlot()
        {
            int fading = -1;
            for (int i = 0; i < PoolSize; i++)
            {
                if (_owned[i])
                {
                    continue;
                }

                if (_leftAt[i] < 0f)
                {
                    return i;
                }

                if (fading < 0 || _leftAt[i] < _leftAt[fading])
                {
                    fading = i;
                }
            }

            return fading;
        }

        private void Release(int i, float now)
        {
            _owned[i] = false;
            _owner[i] = null;
            _leftAt[i] = now;
            UiMotion.Alpha(_groups[i], 0f, UiDuration.Fast, UiEase.Exit, UiMotionClass.Decor);
        }

        private void Style(int i, State state)
        {
            _shown[i] = state;
            // Literals: no formatting. Cream for both - the silhouette and the word carry the difference (8-R).
            _labels[i].text = state == State.Netted ? "NET" : "DAZED";
            if (_glyphs[i] != null)
            {
                UiArt.SetGlyph(_glyphs[i], state == State.Netted ? _nettedSprite : _dazedSprite);
                UiArt.ApplyGlyph(_glyphs[i], new Vector3(0.25f, 0f, 0f));
            }
        }
    }
}
