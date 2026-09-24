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
    /// Zero allocation per frame (ADR-037-1): a fixed pool of chips built once, the
    /// live-zombie list is indexed (no enumerator), texts are pre-built literals swapped
    /// only when a marker's state changes, and an unused chip is parked off-screen rather
    /// than SetActive-toggled (the WP-022 reticle lesson - no canvas rebuild).
    /// </summary>
    public sealed class UiEnemyStatusMarkers : MonoBehaviour
    {
        public const int PoolSize = 16;
        private const float HeadHeight = 2.1f;
        private const float ChipWidth = 88f;
        private const float GlyphSize = 18f; // WP-022L: AP-012 v003 Status.Netted / Status.Dazed glyph, left of the word
        private const float ChipHeight = 24f; // WP-022L (AD v007 MUST-2): room for the 18 px glyph
        private static readonly Vector2 Parked = new Vector2(-10000f, -10000f);

        private enum State { None = 0, Netted = 1, Dazed = 2 }

        private RectTransform[] _chips;
        private Image[] _fills;
        private Image[] _glyphs;
        private Sprite _nettedSprite;
        private Sprite _dazedSprite;
        private TMP_Text[] _labels;
        private State[] _shown;
        private RectTransform _canvas;
        private bool _ink;

        /// <summary>Markers drawn in the last update (test readout).</summary>
        public int VisibleCount { get; private set; }

        /// <param name="ink">
        /// WP-068 8-R (AD MUST-3): the boxless stamp of the concepts. It follows the threat-chip recipe:
        /// - the outer-only glyph (Status.*Outer, SDF ink edge) and the word, in cream with the HUD ink;
        /// - a brush underline;
        /// - no box.
        /// A status is not a threat, so there is no state colour; shape and word tell NET from DAZED.
        /// </param>
        public void Configure(RectTransform canvasRect, bool ink = false)
        {
            _canvas = canvasRect;
            _ink = ink;
            _chips = new RectTransform[PoolSize];
            _fills = new Image[PoolSize];
            _glyphs = new Image[PoolSize];
            _nettedSprite = (ink ? UiArt.Icon("Status.NettedOuter") : null) ?? UiArt.Icon("Status.Netted");
            _dazedSprite = (ink ? UiArt.Icon("Status.DazedOuter") : null) ?? UiArt.Icon("Status.Dazed");
            bool glyphs = _nettedSprite != null && _dazedSprite != null;
            _labels = new TMP_Text[PoolSize];
            _shown = new State[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                TMP_Text label = UiFactory.Chip("Status." + i, transform, "NET", UiColor.SurfaceInset, UiColor.StateWarning, UiFactory.ScaledBand(ChipWidth), UiFactory.ScaledBand(ChipHeight));
                label.fontStyle = FontStyles.Bold;
                var chip = (RectTransform)label.transform.parent;
                chip.anchorMin = new Vector2(0.5f, 0.5f);
                chip.anchorMax = new Vector2(0.5f, 0.5f);
                chip.pivot = new Vector2(0.5f, 0f);
                chip.anchoredPosition = Parked;
                _chips[i] = chip;
                _fills[i] = chip.GetComponent<Image>();
                _labels[i] = label;
                if (glyphs)
                {
                    // Word + glyph (never colour alone): the glyph sits in the chip's left inset
                    // and the label's own left inset moves past it. Sprites swap by reference.
                    Image glyph = UiArt.IconImage("Icon", chip, "Status.Netted", UiColor.StateWarning, GlyphSize);
                    var glyphRect = (RectTransform)glyph.transform;
                    glyphRect.anchorMin = new Vector2(0f, 0.5f);
                    glyphRect.anchorMax = new Vector2(0f, 0.5f);
                    glyphRect.pivot = new Vector2(0f, 0.5f);
                    glyphRect.anchoredPosition = new Vector2((int)UiSpace.XS, 0f);
                    var labelRect = (RectTransform)label.transform;
                    labelRect.offsetMin = new Vector2((int)UiSpace.XS + GlyphSize + (int)UiSpace.XS, 0f);
                    _glyphs[i] = glyph;
                }

                if (ink)
                {
                    Color clear = _fills[i].color;
                    clear.a = 0f;
                    _fills[i].color = clear;
                    UiHudInk.Apply(label);
                    if (_glyphs[i] != null && UiArt.IsGlyph(_glyphs[i].sprite))
                    {
                        UiArt.ApplyGlyph(_glyphs[i], new Vector3(0.25f, 0f, 0f));
                    }

                    Sprite brush = UiFxGlobals.InkMask(UiInkChannel.Underline);
                    Image underline = UiFactory.NewImage("Underline", chip, UiColor.TextPrimary, brush != null ? brush : UiSprites.Solid);
                    var underlineRect = underline.rectTransform;
                    underlineRect.anchorMin = new Vector2(0f, 0f);
                    underlineRect.anchorMax = new Vector2(1f, 0f);
                    underlineRect.pivot = new Vector2(0.5f, 0.5f);
                    underlineRect.offsetMin = new Vector2((int)UiSpace.XS + GlyphSize + (int)UiSpace.XS, 0f);
                    underlineRect.offsetMax = new Vector2(-(int)UiSpace.S, 4f);
                    Color line = underline.color;
                    line.a = 0.8f;
                    underline.color = line;
                    underline.transform.SetSiblingIndex(0);
                }
                _shown[i] = State.None;
            }
        }

        private void LateUpdate()
        {
            if (_chips == null || _canvas == null)
            {
                return;
            }

            Camera camera = Camera.main;
            System.Collections.Generic.IReadOnlyList<ZombieEnemy> live = ZombieEnemy.Live;
            int used = 0;
            if (camera != null && live != null)
            {
                Vector2 canvasSize = _canvas.rect.size;
                for (int i = 0; i < live.Count && used < PoolSize; i++)
                {
                    ZombieEnemy z = live[i];
                    if (z == null || !z.IsAlive)
                    {
                        continue;
                    }

                    State state = z.IsNetted ? State.Netted : z.IsDazed ? State.Dazed : State.None;
                    if (state == State.None)
                    {
                        continue;
                    }

                    Vector3 viewport = camera.WorldToViewportPoint(z.transform.position + Vector3.up * HeadHeight);
                    if (viewport.z < 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                    {
                        continue;
                    }

                    RectTransform chip = _chips[used];
                    chip.anchoredPosition = new Vector2((viewport.x - 0.5f) * canvasSize.x, (viewport.y - 0.5f) * canvasSize.y);
                    if (_shown[used] != state)
                    {
                        _shown[used] = state;
                        // Literals: no formatting. NET = warning gold (held), DAZED = danger coral (stunned).
                        _labels[used].text = state == State.Netted ? "NET" : "DAZED";
                        _labels[used].color = UiTokens.Get(_ink ? UiColor.TextPrimary : state == State.Netted ? UiColor.StateWarning : UiColor.StateDanger);
                        if (_glyphs[used] != null)
                        {
                            _glyphs[used].sprite = state == State.Netted ? _nettedSprite : _dazedSprite;
                            _glyphs[used].color = _labels[used].color;
                        }
                    }

                    used++;
                }
            }

            for (int i = used; i < PoolSize; i++)
            {
                if (_shown[i] != State.None)
                {
                    _shown[i] = State.None;
                    _chips[i].anchoredPosition = Parked;
                }
            }

            VisibleCount = used;
        }
    }
}
