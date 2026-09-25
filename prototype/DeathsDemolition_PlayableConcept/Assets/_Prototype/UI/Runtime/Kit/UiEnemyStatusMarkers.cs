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
    /// M4-R1 N19 / DEC-094 (the two new Specials; NET / DAZED left with the old ones): SHOCK while Raiju's chain holds a body
    /// (ZombieEnemy.IsElectrocuted), SLIDE while Namekuji's gel carries it, DOWN while it falls, lies or gets up
    /// (ZombieEnemy.Gel). The glyph (Status.Electrocuted / Sliding / Downed, outer twin first) takes its Special's identity
    /// tint (UiGlyphTints: Arc / Gel - "who did this", not a threat colour); the word stays cream. Until the G1 cells ship
    /// the stamps are word-only.
    ///
    /// WP-068 slice 8c · E19 status stamps, in the 8-R ink language (AD 8-R MUST-3):
    /// - Each stamp is the 24 px outer-only silhouette (G1 Status.*Outer, SDF ink edge) plus the word in cream with the HUD
    ///   ink, over a brush underline - no box. A status is not a threat, so there is no state colour: shape and word tell
    ///   the states apart; the glyph's tint only names the Special behind it.
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
    ///
    /// WP-068 12b · where a stamp may stand (<see cref="Resolve"/>): never on an ink cluster (AD 8c "new for 12b") and never on
    /// the player's figure (AD R1-B SHOULD-4). The HUD hands both in through <see cref="Configure"/>, read once a frame and
    /// only while a chip is placed; the stamps' layer sorts under the banner (the HUD's order).
    /// </summary>
    public sealed class UiEnemyStatusMarkers : MonoBehaviour
    {
        public const int PoolSize = 16;
        public const float GlyphSize = 24f;
        /// <summary>The stamp's anchor over a zombie's feet, metres (captures stage it by this height).</summary>
        public const float HeadHeight = 2.1f;
        private const float MinChipWidth = 88f;
        private static readonly Vector2 Parked = new Vector2(-10000f, -10000f);
        private static readonly Vector3[] s_corners = new Vector3[4];

        private enum State { None = 0, Shock = 1, Slide = 2, Down = 3 }

        // R1 content pack: hud.status.shock / slide / down (S-09 wrote them back, pack §5 note).
        private static readonly string[] Words = { string.Empty, "SHOCK", "SLIDE", "DOWN" };
        private static readonly string[] GlyphKeys = { null, "Status.Electrocuted", "Status.Sliding", "Status.Downed" };

        private RectTransform[] _chips;
        private CanvasGroup[] _groups;
        private Image[] _glyphs;
        private readonly Sprite[] _sprites = new Sprite[4];
        private TMP_Text[] _labels;
        private State[] _shown;
        private ZombieEnemy[] _owner;
        private bool[] _owned;
        private float[] _leftAt;
        private RectTransform _canvas;
        private float _exitSeconds;

        /// <summary>WP-068 12b: fills <paramref name="into"/> with the rects a stamp keeps clear of (canvas px, centre origin); returns the count.</summary>
        public delegate int AvoidRects(Rect[] into);

        /// <summary>WP-068 12b: the player figure's rect on the canvas (px, centre origin); false when no figure is on screen.</summary>
        public delegate bool FigureRect(out Rect figure);

        /// <summary>AD 8c (new for 12b): a stamp keeps this far from an ink cluster - the edge marker's clearance.</summary>
        public const float ClusterClearance = UiWorldMarker.ClusterClearance;

        /// <summary>AD R1-B SHOULD-4: a stamp that comes this close to the player figure lifts to the band above it.</summary>
        public const float FigureClearance = 24f;

        private const int ResolvePasses = 8;
        private AvoidRects _avoidSource;
        private FigureRect _figureSource;
        private readonly Rect[] _avoid = new Rect[8];
        private int _avoidCount;
        private bool _hasFigure;
        private Rect _figure;
        private int _clearanceFrame = -1;

        /// <summary>Markers bound to a body in the last update (test readout; a fading chip no longer counts).</summary>
        public int VisibleCount { get; private set; }

        /// <param name="canvasRect">The HUD canvas the chips' positions are measured on (centre origin).</param>
        /// <param name="avoid">WP-068 12b: the HUD's ink clusters (null: none).</param>
        /// <param name="figure">WP-068 12b: the player figure as the camera sees it (null: none).</param>
        public void Configure(RectTransform canvasRect, AvoidRects avoid = null, FigureRect figure = null)
        {
            _canvas = canvasRect;
            _avoidSource = avoid;
            _figureSource = figure;
            _chips = new RectTransform[PoolSize];
            _groups = new CanvasGroup[PoolSize];
            _glyphs = new Image[PoolSize];
            bool glyphs = true;
            for (int s = 1; s < GlyphKeys.Length; s++)
            {
                _sprites[s] = UiArt.Icon(GlyphKeys[s] + "Outer") ?? UiArt.Icon(GlyphKeys[s]);
                glyphs &= _sprites[s] != null;
            }
            _labels = new TMP_Text[PoolSize];
            _shown = new State[PoolSize];
            _owner = new ZombieEnemy[PoolSize];
            _owned = new bool[PoolSize];
            _leftAt = new float[PoolSize];
            _exitSeconds = UiMotionTokens.Seconds(UiDuration.Fast);

            // E19: the chip is as tall as the glyph plus a 2 px margin, or the word's line at this TextScale if taller;
            // as wide as glyph + the longest word, never under the old 88.
            float glyph = glyphs ? GlyphSize : 0f;
            float height = Mathf.Max(GlyphSize + 4f, UiFactory.ScaledBand(UiFactory.LineHeight(UiType.Label)));
            Sprite brush = UiFxGlobals.InkMask(UiInkChannel.Underline);
            for (int i = 0; i < PoolSize; i++)
            {
                TMP_Text label = UiFactory.Chip("Status." + i, transform, Words[1], UiColor.SurfaceInset, UiColor.TextPrimary, MinChipWidth, height);
                label.fontStyle = FontStyles.Bold;
                UiHudInk.Apply(label);
                float words = 0f;
                for (int w = 1; w < Words.Length; w++)
                {
                    words = Mathf.Max(words, Mathf.Ceil(label.GetPreferredValues(Words[w]).x));
                }

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
                label.text = Words[1];
                if (glyphs)
                {
                    // Word + glyph (never colour alone): the glyph sits in the chip's left inset
                    // and the label's own left inset moves past it.
                    Image image = UiFactory.NewImage("Icon", chip, UiColor.TextPrimary, _sprites[1]);
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
                    _chips[i].anchoredPosition = Clear(at, i);
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
                    _chips[slot].anchoredPosition = Clear(at, slot);
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

        /// <summary>The chip's place past the clusters and the figure; the HUD's rects are read once a frame, on the first chip placed.</summary>
        private Vector2 Clear(Vector2 at, int i)
        {
            if (_avoidSource == null && _figureSource == null)
            {
                return at;
            }

            if (_clearanceFrame != Time.frameCount)
            {
                _clearanceFrame = Time.frameCount;
                _avoidCount = _avoidSource != null ? Mathf.Min(_avoidSource(_avoid), _avoid.Length) : 0;
                _hasFigure = _figureSource != null && _figureSource(out _figure);
            }

            Vector2 placed = Resolve(at, _chips[i].sizeDelta, _avoid, _avoidCount, _hasFigure, _figure);
            LastPlaced = new Rect(placed.x - _chips[i].sizeDelta.x * 0.5f, placed.y, _chips[i].sizeDelta.x, _chips[i].sizeDelta.y);
            return placed;
        }

        /// <summary>The last stamp this frame resolved, canvas px (centre origin; bottom edge at y) - capture logs.</summary>
        public static Rect LastPlaced { get; private set; }

        /// <summary>
        /// WP-068 12b · where a stamp stands, in canvas px with the origin at the centre. <paramref name="at"/> is the head point
        /// the chip rises from (its pivot is the bottom centre), <paramref name="chipSize"/> its size. x never changes: the stamp
        /// stays over its body's column (the E19 projection rule). Only y resolves:
        /// - AD R1-B SHOULD-4: a chip that meets the player figure + <see cref="FigureClearance"/> lifts to the band above it.
        ///   Nothing is drawn on the player model (the 8-R slot map), and the pooled crowd follows the same rule.
        /// - AD 8c (new for 12b): a chip that meets an ink cluster + <see cref="ClusterClearance"/> (the edge marker's
        ///   AvoidOnEdge clearance) leaves it toward the middle of the screen: above a cluster in the lower half, below one in
        ///   the upper half.
        /// Up to 8 passes, so a chip moved off one rect checks the others again (first person: off the health block, then off
        /// the weapon cluster above it). Allocation-free; public for the sweep test.
        /// </summary>
        public static Vector2 Resolve(Vector2 at, Vector2 chipSize, Rect[] clusters, int count, bool hasFigure, Rect figure)
        {
            float half = chipSize.x * 0.5f;
            for (int pass = 0; pass < ResolvePasses; pass++)
            {
                bool moved = false;
                if (hasFigure && new Rect(at.x - half, at.y, chipSize.x, chipSize.y).Overlaps(Grown(figure, FigureClearance)))
                {
                    at.y = figure.yMax + FigureClearance;
                    moved = true;
                }

                for (int i = 0; i < count; i++)
                {
                    Rect clear = Grown(clusters[i], ClusterClearance);
                    if (!new Rect(at.x - half, at.y, chipSize.x, chipSize.y).Overlaps(clear))
                    {
                        continue;
                    }

                    at.y = clear.center.y < 0f ? clear.yMax : clear.yMin - chipSize.y;
                    moved = true;
                }

                if (!moved)
                {
                    break;
                }
            }

            return at;
        }

        private static Rect Grown(Rect r, float by)
        {
            return Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);
        }

        private static State StateOf(ZombieEnemy z)
        {
            if (z == null || !z.IsAlive)
            {
                return State.None;
            }

            // A shock holds a slide still (WP-076 B5), so SHOCK outranks the gel states.
            if (z.IsElectrocuted)
            {
                return State.Shock;
            }

            GelState gel = z.Gel;
            return gel == GelState.Sliding ? State.Slide : gel != GelState.None ? State.Down : State.None;
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
            // Literals: no formatting. The word is cream; the glyph wears its Special's identity tint (N19 §2).
            _labels[i].text = Words[(int)state];
            if (_glyphs[i] != null)
            {
                UiArt.SetGlyph(_glyphs[i], _sprites[(int)state]);
                UiArt.ApplyGlyph(_glyphs[i], new Vector3(0.25f, 0f, 0f));
                _glyphs[i].color = UiTokens.Get(state == State.Shock ? UiGlyphTints.ChainLightning : UiGlyphTints.Gel);
            }
        }
    }
}
