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
        private const float ChipWidth = 64f;
        private const float ChipHeight = 20f;
        private static readonly Vector2 Parked = new Vector2(-10000f, -10000f);

        private enum State { None = 0, Netted = 1, Dazed = 2 }

        private RectTransform[] _chips;
        private Image[] _fills;
        private TMP_Text[] _labels;
        private State[] _shown;
        private RectTransform _canvas;

        /// <summary>Markers drawn in the last update (test readout).</summary>
        public int VisibleCount { get; private set; }

        public void Configure(RectTransform canvasRect)
        {
            _canvas = canvasRect;
            _chips = new RectTransform[PoolSize];
            _fills = new Image[PoolSize];
            _labels = new TMP_Text[PoolSize];
            _shown = new State[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                TMP_Text label = UiFactory.Chip("Status." + i, transform, "NET", UiColor.SurfaceInset, UiColor.StateWarning, ChipWidth, ChipHeight);
                label.fontStyle = FontStyles.Bold;
                var chip = (RectTransform)label.transform.parent;
                chip.anchorMin = new Vector2(0.5f, 0.5f);
                chip.anchorMax = new Vector2(0.5f, 0.5f);
                chip.pivot = new Vector2(0.5f, 0f);
                chip.anchoredPosition = Parked;
                _chips[i] = chip;
                _fills[i] = chip.GetComponent<Image>();
                _labels[i] = label;
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
                        _labels[used].color = UiTokens.Get(state == State.Netted ? UiColor.StateWarning : UiColor.StateDanger);
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
