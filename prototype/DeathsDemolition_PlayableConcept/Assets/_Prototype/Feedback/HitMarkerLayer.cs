using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.Feedback
{
    /// <summary>
    /// Deliverable 5: plain, readable hit confirmation.
    ///
    /// Four short strokes that spring outward from the hit point and fade in about a
    /// sixth of a second. It reads at a glance, it does not survive long enough to
    /// stack into noise, and it says three different things without needing a legend:
    ///
    ///   white  - the shot landed on flesh
    ///   steel  - the shot landed on armour and the plate held
    ///   amber  - critical (unarmoured head)
    ///   red X  - the target died
    ///
    /// Placed at the screen projection of the impact point rather than at screen centre.
    /// This camera aims with a cursor at a distance, so a centre marker would confirm a
    /// hit somewhere the player is not looking.
    ///
    /// Its own fixed-size ring of markers, not a PoolService pool: UI lives under a
    /// Canvas and PoolService re-parents to a non-Canvas root on release, which would
    /// mean a layout rebuild per marker. The ring is a pool with the same guarantees and
    /// no reparenting.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitMarkerLayer : MonoBehaviour
    {
        /// <summary>PROTOTYPE-ONLY: markers alive at once. Beyond this the oldest is reused.</summary>
        private const int Capacity = 12;

        private const int StrokesPerMarker = 4;

        /// <summary>PROTOTYPE-ONLY: seconds a marker is visible.</summary>
        private const float MarkerLife = 0.17f;

        /// <summary>PROTOTYPE-ONLY: minimum seconds between two markers. Stops a 15/s weapon from strobing.</summary>
        private const float MinInterval = 0.035f;

        private static readonly Color FleshColor = new Color(1f, 1f, 1f, 0.9f);
        // WP-047D: armour / critical / kill come from FeedbackPalette (S-09 UiTokens, colour-blind safe option).

        private sealed class Marker
        {
            public RectTransform Root;
            public Image[] Strokes;
            public float Age;
            public float Life;
            public bool Active;
            public Color Color;
            public float Spread;
            public float Length;
        }

        private Canvas _canvas;
        private RectTransform _canvasRect;
        private Marker[] _markers;
        private int _next;
        private float _lastShowTime = -999f;

        /// <summary>Turn confirmation off entirely, e.g. from the F1 panel.</summary>
        public bool Enabled { get; set; } = true;

        private void Awake()
        {
            BuildCanvas();
            BuildMarkers();
            PrewarmRegistry();
        }

        /// <summary>
        /// WP-069 (hit path 0 B): every marker shown and hidden once at load. uGUI registers a stroke with the canvas's
        /// graphic set on its first enable (a disable keeps it there), so without this the set's list / dictionary grew
        /// during play, the first time each ring slot showed — a lazy allocation on the hit path.
        /// </summary>
        private void PrewarmRegistry()
        {
            for (int i = 0; i < _markers.Length; i++) _markers[i].Root.gameObject.SetActive(true);
            for (int i = 0; i < _markers.Length; i++) _markers[i].Root.gameObject.SetActive(false);
        }

        private void BuildCanvas()
        {
            _canvas = gameObject.GetComponent<Canvas>();
            if (_canvas == null)
            {
                _canvas = gameObject.AddComponent<Canvas>();
            }

            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Below the HUD on purpose: the HUD task owns the top of the stack and a
            // confirmation flash must never draw over health or ammo.
            _canvas.sortingOrder = 50;

            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Deliberately NO GraphicRaycaster: this layer must never intercept a click.
            _canvasRect = (RectTransform)transform;
        }

        private void BuildMarkers()
        {
            _markers = new Marker[Capacity];

            for (int i = 0; i < Capacity; i++)
            {
                var go = new GameObject("HitMarker", typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(_canvasRect, false);
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = Vector2.zero;
                go.SetActive(false);

                var strokes = new Image[StrokesPerMarker];
                for (int s = 0; s < StrokesPerMarker; s++)
                {
                    var strokeGo = new GameObject("Stroke", typeof(RectTransform), typeof(Image));
                    var strokeRect = (RectTransform)strokeGo.transform;
                    strokeRect.SetParent(rect, false);
                    strokeRect.anchorMin = new Vector2(0.5f, 0.5f);
                    strokeRect.anchorMax = new Vector2(0.5f, 0.5f);
                    strokeRect.pivot = new Vector2(0.5f, 0.5f);

                    var image = strokeGo.GetComponent<Image>();
                    image.raycastTarget = false;
                    image.color = Color.white;
                    strokes[s] = image;
                }

                _markers[i] = new Marker { Root = rect, Strokes = strokes };
            }
        }

        /// <summary>Show a confirmation for a hit that landed at <paramref name="worldPoint"/>.</summary>
        public void Show(Vector3 worldPoint, HitFeedbackTier tier)
        {
            if (!Enabled)
            {
                return;
            }

            if (Time.unscaledTime - _lastShowTime < MinInterval)
            {
                return;
            }

            Camera cam = FeedbackCameraRef.Current;
            if (cam == null)
            {
                return;
            }

            Vector3 screen = cam.WorldToScreenPoint(worldPoint);
            if (screen.z <= 0f)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRect, screen, null, out Vector2 local))
            {
                return;
            }

            _lastShowTime = Time.unscaledTime;

            Marker marker = _markers[_next];
            _next = (_next + 1) % Capacity;

            marker.Root.anchoredPosition = local;
            marker.Age = 0f;
            marker.Life = MarkerLife;
            marker.Active = true;

            switch (tier)
            {
                case HitFeedbackTier.ArmorHit:
                    marker.Color = FeedbackPalette.WithAlpha(FeedbackPalette.Armor, 0.95f);   // WP-047D: palette token (colour-blind safe option)
                    marker.Spread = 10f;
                    marker.Length = 12f;
                    break;

                case HitFeedbackTier.ArmorBreak:
                    marker.Color = FeedbackPalette.WithAlpha(FeedbackPalette.Armor, 0.95f);
                    marker.Spread = 14f;
                    marker.Length = 18f;
                    break;

                case HitFeedbackTier.CriticalHit:
                    marker.Color = FeedbackPalette.Critical;
                    marker.Spread = 12f;
                    marker.Length = 17f;
                    break;

                case HitFeedbackTier.Death:
                case HitFeedbackTier.HeavyDeath:
                    marker.Color = FeedbackPalette.Kill;
                    marker.Spread = 16f;
                    marker.Length = tier == HitFeedbackTier.HeavyDeath ? 26f : 20f;
                    break;

                default:
                    marker.Color = FleshColor;
                    marker.Spread = 9f;
                    marker.Length = 11f;
                    break;
            }

            marker.Root.gameObject.SetActive(true);
            Layout(marker, 0f);

            AudioEvents.Raise(
                tier >= HitFeedbackTier.Death ? CombatAudioEvent.KillMarker : CombatAudioEvent.HitMarker,
                worldPoint, 1f, 10);
        }

        private void Update()
        {
            // Unscaled: the confirmation must not stretch during a hit stop, or a heavy
            // kill would leave a marker hanging on screen well after the impact.
            float dt = Time.unscaledDeltaTime;

            for (int i = 0; i < _markers.Length; i++)
            {
                Marker m = _markers[i];
                if (!m.Active)
                {
                    continue;
                }

                m.Age += dt;
                float t = Mathf.Clamp01(m.Age / m.Life);

                if (t >= 1f)
                {
                    m.Active = false;
                    m.Root.gameObject.SetActive(false);
                    continue;
                }

                Layout(m, t);
            }
        }

        private static void Layout(Marker m, float t)
        {
            // Springs outward fast, then eases. The pop is the confirmation.
            float outward = m.Spread * (1f + 0.85f * Mathf.Sin(t * Mathf.PI * 0.5f));
            float alpha = 1f - t * t;

            Color c = m.Color;
            c.a *= alpha;

            for (int s = 0; s < m.Strokes.Length; s++)
            {
                Image image = m.Strokes[s];
                var rect = (RectTransform)image.transform;

                float angle = 45f + s * 90f;
                Vector2 dir = new Vector2(
                    Mathf.Cos(angle * Mathf.Deg2Rad),
                    Mathf.Sin(angle * Mathf.Deg2Rad));

                rect.sizeDelta = new Vector2(3f, m.Length);
                rect.anchoredPosition = dir * (outward + m.Length * 0.5f);
                rect.localRotation = Quaternion.Euler(0f, 0f, -angle + 90f);

                image.color = c;
            }
        }

        /// <summary>Creates the layer if it does not exist yet. Survives scene loads.</summary>
        public static HitMarkerLayer EnsureExists()
        {
            HitMarkerLayer existing = FindObjectOfType<HitMarkerLayer>();
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject("[Hit Markers]", typeof(RectTransform));
            DontDestroyOnLoad(go);
            return go.AddComponent<HitMarkerLayer>();
        }
    }
}
