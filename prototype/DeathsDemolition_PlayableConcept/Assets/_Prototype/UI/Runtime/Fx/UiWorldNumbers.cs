using DD.Prototype.Feedback;
using DD.Prototype.PlayerSystem;
using DD.Prototype.Rounds;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>WP-068 E18 · WorldNumber type sizes (direction §2.2): S 18 · M 22 · L 26 · XL 30.</summary>
    public enum UiWorldNumberSize
    {
        S = 0,
        M = 1,
        L = 2,
        XL = 3
    }

    /// <summary>E18b · the glyph beside a number (direction §5): the armour-break plates, the Elite / Ultra tier head (outer-only).</summary>
    public enum UiWorldNumberGlyph
    {
        None = 0,
        ArmorBreak = 1,
        Elite = 2,
        Ultra = 3
    }

    /// <summary>How one damage number reads (GD hierarchy, direction §5): size, colour token, kill weight, crit starburst, glyph.</summary>
    public readonly struct UiWorldNumberStyle
    {
        public readonly UiWorldNumberSize Size;
        public readonly UiColor Color;
        public readonly bool Bold;
        public readonly bool Burst;
        public readonly UiWorldNumberGlyph Glyph;

        public UiWorldNumberStyle(UiWorldNumberSize size, UiColor color, bool bold = false, bool burst = false, UiWorldNumberGlyph glyph = UiWorldNumberGlyph.None)
        {
            Size = size;
            Color = color;
            Bold = bold;
            Burst = burst;
            Glyph = glyph;
        }

        /// <summary>A merge keeps the loudest read: the larger size, a kill's weight, a crit's starburst, the heavier glyph; the latest colour.</summary>
        public UiWorldNumberStyle Merge(UiWorldNumberStyle next)
        {
            return new UiWorldNumberStyle(next.Size > Size ? next.Size : Size, next.Color, Bold || next.Bold, Burst || next.Burst,
                next.Glyph > Glyph ? next.Glyph : Glyph);
        }
    }

    /// <summary>
    /// WP-068 slice 8 · E18 damage numbers (S-07, direction §5 E18). The world <c>TextMesh</c> numbers become pooled
    /// <c>TextMeshProUGUI</c> on ONE overlay canvas at <see cref="UiLayer.WorldNumber"/> (40, under the HUD), capacity
    /// <see cref="Capacity"/>, one shared Oswald material with the house FaceOutline (1 px InkStroke + 2 px shadow), so the
    /// numbers are one batch (crit starbursts sit in their own container under them: a second batch only while one shows).
    /// Motion: anchored to the hit point, offset upward only (a sideways drift would credit a neighbour); 24 px rise over
    /// 600 ms Settle, alpha out over the last 200 ms, pop 1.15 → 1 in 100 ms, all on the scaled clock (a hit-stop holds
    /// them). ≤ <see cref="MaxVisible"/> on screen (the oldest gives way). <c>PhaseChanged</c> → RoundComplete / Dead
    /// flushes every number with a 200 ms unscaled fade (bot-upgrade: three "22"s frozen through ROUND 1 COMPLETE).
    /// Reduced motion: the number appears at rest for 500 ms, then fades. A number never sits on the player (DEC-077):
    /// inside the player's screen box it is pushed above the head. Silent. Zero allocation per hit: cached strings,
    /// alpha through the CanvasRenderer, no layout.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public sealed class UiWorldNumbers : MonoBehaviour
    {
        public const int Capacity = 48;
        public const int MaxVisible = 12;
        public const float RisePx = 24f;
        public const float LifeSeconds = 0.6f;
        public const float FadeSeconds = 0.2f;
        public const float PopFrom = 1.15f;
        public const float PopSeconds = 0.1f;
        public const float FlushSeconds = 0.2f;
        public const float RestSeconds = 0.5f;
        public const float BurstScale = 2.3f;
        /// <summary>E18b: glyph height as a share of the point size, and the gap to the number (reference px).</summary>
        public const float GlyphScale = 1.1f;
        /// <summary>E18b (AD SHOULD 1): S / M glyphs a little larger so the plates resolve at 22 px.</summary>
        public const float GlyphScaleSmall = 1.25f;
        /// <summary>SdfGlyph outline width (distance-field units) ≈ 1 px at the glyph sizes in use.</summary>
        public const float GlyphOutline = 0.2f;
        public const float GlyphGap = 6f;
        /// <summary>E18b: clearance above a status stamp's rect (screen px).</summary>
        public const float AvoidGap = 4f;
        public const int AvoidCapacity = 64;

        /// <summary>The G1 glyph keys (UiArt.Icon) of <see cref="UiWorldNumberGlyph"/>.</summary>
        public static readonly string[] GlyphKeys = { null, "Ach.PlateWrecker", "Threat.EliteOuter", "Threat.UltraOuter" };

        private static readonly int[] s_pointSizes = { 18, 22, 26, 30 };

        /// <summary>The WorldNumber point size of a size step.</summary>
        public static int PointSize(UiWorldNumberSize size) => s_pointSizes[(int)size];

        private enum SlotState : byte
        {
            Free,
            Live,
            Flushing
        }

        private struct Slot
        {
            public TextMeshProUGUI Text;
            public RectTransform TextRect;
            public CanvasRenderer TextRenderer;
            public Image Burst;
            public RectTransform BurstRect;
            public CanvasRenderer BurstRenderer;
            public Image Glyph;
            public RectTransform GlyphRect;
            public CanvasRenderer GlyphRenderer;
            public SlotState State;
            public int TargetId;
            public Vector3 Anchor;
            public float Amount;
            public float Age;
            public float SinceAdd;
            public float PopAge;
            public float FlushAge;
            public float FlushFrom;
            public float Alpha;
            public int Sequence;
            public UiWorldNumberStyle Style;
            public string Assigned;
        }

        private static UiWorldNumbers s_instance;
        private static Material s_material;
        private static Sprite s_burst;

        /// <summary>Tests: the camera to project with instead of the gameplay camera.</summary>
        public static Camera CameraOverride;

        private readonly Slot[] _slots = new Slot[Capacity];

        private struct AvoidRect
        {
            public int TargetId;
            public Rect Screen;
            public int Frame;
        }

        private readonly AvoidRect[] _avoid = new AvoidRect[AvoidCapacity];
        private readonly Sprite[] _glyphs = new Sprite[4];
        private RectTransform _root;
        private int _sequence;

        public static UiWorldNumbers Instance
        {
            get
            {
                if (s_instance == null)
                {
                    var go = new GameObject("[UiWorldNumbers]");
                    DontDestroyOnLoad(go);
                    s_instance = go.AddComponent<UiWorldNumbers>();
                }

                return s_instance;
            }
        }

        public static bool Exists => s_instance != null;

        /// <summary>The one canvas the numbers draw on.</summary>
        public Canvas Canvas { get; private set; }

        /// <summary>The shared number material (one batch).</summary>
        public static Material SharedMaterial => s_material;

        /// <summary>Numbers on screen now (live, not flushing).</summary>
        public int VisibleCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].State == SlotState.Live) n++;
                }

                return n;
            }
        }

        /// <summary>Numbers still drawing (live or fading out of a flush).</summary>
        public int DrawingCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i].State != SlotState.Free) n++;
                }

                return n;
            }
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            Build();
            RunEvents.PhaseChanged += OnPhaseChanged;
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDestroy()
        {
            RunEvents.PhaseChanged -= OnPhaseChanged;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        private void Build()
        {
            Canvas = UiFactory.CreateCanvas("WorldNumbersCanvas", UiLayer.WorldNumber, transform);
            GraphicRaycaster raycaster = Canvas.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = false;   // numbers are never a click target
            }

            _root = (RectTransform)Canvas.transform;
            RectTransform bursts = UiFactory.NewRect("Bursts", _root);
            UiFactory.Fill(bursts);
            RectTransform glyphs = UiFactory.NewRect("Glyphs", _root);
            UiFactory.Fill(glyphs);
            RectTransform numbers = UiFactory.NewRect("Numbers", _root);
            UiFactory.Fill(numbers);
            for (int g = 1; g < GlyphKeys.Length; g++)
            {
                _glyphs[g] = UiArt.Icon(GlyphKeys[g]);   // G1 SDF atlas: every glyph on one texture → one batch
            }

            TMP_FontAsset font = UiFonts.Role(UiFontRole.Numeric) ?? UiFonts.For(UiType.NumericM);
            Material material = NumberMaterial(font);
            Sprite burst = BurstSprite();
            for (int i = 0; i < _slots.Length; i++)
            {
                var textGo = new GameObject("N" + i, typeof(RectTransform), typeof(TextMeshProUGUI));
                var rt = (RectTransform)textGo.transform;
                rt.SetParent(numbers, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(160f, 48f);
                var text = textGo.GetComponent<TextMeshProUGUI>();
                if (font != null) text.font = font;
                if (material != null) text.fontSharedMaterial = material;
                text.alignment = TextAlignmentOptions.Center;
                text.enableWordWrapping = false;
                text.overflowMode = TextOverflowModes.Overflow;
                text.enableAutoSizing = false;
                text.raycastTarget = false;
                text.richText = false;
                text.extraPadding = true;
                text.fontSize = PointSize(UiWorldNumberSize.M);
                text.text = "0";

                Image img = UiFactory.NewImage("B" + i, bursts, UiColor.TextPrimary, burst);
                img.color = Color.white;   // two-tone sprite (ink body, cream rim) built from the tokens
                img.raycastTarget = false;
                var brt = (RectTransform)img.transform;
                brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);

                // The glyph image is built with the first available G1 sprite so UiFactory gives it the atlas's SdfGlyph Fx.
                Sprite any = _glyphs[1] ?? _glyphs[2] ?? _glyphs[3];
                Image glyph = UiFactory.NewImage("G" + i, glyphs, UiColor.TextPrimary, any);
                glyph.raycastTarget = false;
                glyph.preserveAspect = true;
                var grt = (RectTransform)glyph.transform;
                grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(0.5f, 0.5f);
                glyph.enabled = false;
                // The numbers' ink edge on the glyph (AD E18b MUST 1): G1 cells are 64 px with a 6 px spread, drawn at
                // 24–33 px → 1 screen px ≈ 0.2 of the field; the ring is the Fx ink (Sumi).
                UiArt.ApplyGlyph(glyph, new Vector3(GlyphOutline, 0f, 0f));

                _slots[i] = new Slot
                {
                    Text = text,
                    TextRect = rt,
                    TextRenderer = text.canvasRenderer,
                    Burst = img,
                    BurstRect = brt,
                    BurstRenderer = img.canvasRenderer,
                    Glyph = glyph,
                    GlyphRect = grt,
                    GlyphRenderer = glyph.canvasRenderer,
                    State = SlotState.Free
                };
                Hide(ref _slots[i]);
            }
        }

        // ------------------------------------------------------------------ API

        /// <summary>
        /// Fold a hit into the live number on the same target when its last hit was within <paramref name="mergeWindow"/>
        /// (scaled s): the amount adds up, the style keeps the loudest read, the number re-pops and restarts its life.
        /// </summary>
        public bool TryMerge(int targetId, float amount, UiWorldNumberStyle style, Vector3 worldPoint, float mergeWindow, float anchorBlend = 0.35f)
        {
            if (targetId == 0)
            {
                return false;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot s = ref _slots[i];
                if (s.State != SlotState.Live || s.TargetId != targetId || s.SinceAdd > mergeWindow)
                {
                    continue;
                }

                s.Amount += amount;
                s.Anchor = Vector3.Lerp(s.Anchor, worldPoint, anchorBlend);
                s.Style = s.Style.Merge(style);
                s.Age = 0f;
                s.SinceAdd = 0f;
                s.PopAge = 0f;
                s.Sequence = ++_sequence;
                ApplyStyle(ref s);
                return true;
            }

            return false;
        }

        /// <summary>A new number at a world point. At <see cref="MaxVisible"/> the oldest gives way.</summary>
        public void Spawn(int targetId, float amount, UiWorldNumberStyle style, Vector3 worldPoint)
        {
            int free = -1, oldest = -1, live = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                SlotState st = _slots[i].State;
                if (st == SlotState.Free)
                {
                    if (free < 0) free = i;
                    continue;
                }

                if (st == SlotState.Live)
                {
                    live++;
                    if (oldest < 0 || _slots[i].Sequence < _slots[oldest].Sequence) oldest = i;
                }
            }

            if (live >= MaxVisible && oldest >= 0)
            {
                Release(ref _slots[oldest]);
                if (free < 0) free = oldest;
            }

            if (free < 0)
            {
                return;   // every slot is fading out of a flush: drop, the round is over anyway
            }

            ref Slot s = ref _slots[free];
            s.State = SlotState.Live;
            s.TargetId = targetId;
            s.Anchor = worldPoint;
            s.Amount = amount;
            s.Age = 0f;
            s.SinceAdd = 0f;
            s.PopAge = 0f;
            s.Alpha = 0f;
            s.Sequence = ++_sequence;
            s.Style = style;
            ApplyStyle(ref s);
            Place(ref s);
        }

        /// <summary>Every number fades out over <see cref="FlushSeconds"/> on the unscaled clock (the world may be frozen).</summary>
        public void Flush()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot s = ref _slots[i];
                if (s.State != SlotState.Live) continue;
                s.State = SlotState.Flushing;
                s.FlushAge = 0f;
                s.FlushFrom = s.Alpha;
            }
        }

        /// <summary>Drop every number at once (scene change, tests).</summary>
        public void Clear()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                Release(ref _slots[i]);
            }
        }

        /// <summary>Tests: the live number's amount / point size / burst on a target (false when none).</summary>
        public bool TryRead(int targetId, out float amount, out float pointSize, out bool burst, out bool bold)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].State == SlotState.Live && _slots[i].TargetId == targetId)
                {
                    amount = _slots[i].Amount;
                    pointSize = _slots[i].Text.fontSize;
                    burst = _slots[i].Burst.enabled;
                    bold = (_slots[i].Text.fontStyle & FontStyles.Bold) != 0;
                    return true;
                }
            }

            amount = pointSize = 0f;
            burst = bold = false;
            return false;
        }

        /// <summary>Tests: the canvas position and alpha of the live number on a target.</summary>
        public bool TryReadPlacement(int targetId, out Vector2 position, out float alpha)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].State != SlotState.Free && _slots[i].TargetId == targetId)
                {
                    position = _slots[i].TextRect.anchoredPosition;
                    alpha = _slots[i].TextRenderer.GetAlpha();
                    return true;
                }
            }

            position = Vector2.zero;
            alpha = 0f;
            return false;
        }

        // ------------------------------------------------------------------ frame

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            float udt = Time.unscaledDeltaTime;
            bool rm = UiMotion.ReducedMotion;
            float life = rm ? RestSeconds + FadeSeconds : LifeSeconds;
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot s = ref _slots[i];
                if (s.State == SlotState.Free) continue;

                if (s.State == SlotState.Flushing)
                {
                    s.FlushAge += udt;
                    s.Alpha = s.FlushFrom * (1f - Mathf.Clamp01(s.FlushAge / FlushSeconds));
                    if (s.FlushAge >= FlushSeconds)
                    {
                        Release(ref s);
                        continue;
                    }
                }
                else
                {
                    s.Age += dt;
                    s.SinceAdd += dt;
                    s.PopAge += dt;
                    if (s.Age >= life)
                    {
                        Release(ref s);
                        continue;
                    }

                    s.Alpha = s.Age < life - FadeSeconds ? 1f : 1f - (s.Age - (life - FadeSeconds)) / FadeSeconds;
                }

                Place(ref s);
            }
        }

        private void Place(ref Slot s)
        {
            Camera cam = CameraOverride != null ? CameraOverride : FeedbackCameraRef.Current;
            if (cam == null)
            {
                SetAlpha(ref s, 0f);
                return;
            }

            Vector3 screen = cam.WorldToScreenPoint(s.Anchor);
            if (screen.z <= 0f)
            {
                SetAlpha(ref s, 0f);   // behind the camera
                return;
            }

            if (CameraOverride == null)
            {
                screen = KeepOffPlayer(cam, screen);
            }

            screen = KeepOffStamp(s.TargetId, screen, PointSize(s.Style.Size) * 0.6f * Canvas.scaleFactor);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, null, out Vector2 local);
            bool rm = UiMotion.ReducedMotion;
            // A flush stops the clock (Age holds), so the number fades where it stands.
            float rise = rm ? 0f : RisePx * UiEaseCurve.Evaluate(UiEase.Settle, s.Age / LifeSeconds);

            Vector2 at = new Vector2(local.x, local.y + rise);
            s.TextRect.anchoredPosition = at;
            float pop = rm || s.State == SlotState.Flushing ? 1f : Mathf.Lerp(PopFrom, 1f, UiEaseCurve.Evaluate(UiEase.Settle, s.PopAge / PopSeconds));
            s.TextRect.localScale = new Vector3(pop, pop, 1f);
            if (s.Burst.enabled)
            {
                s.BurstRect.anchoredPosition = at;   // not scaled by the pop: the 2 px rim stays 2 px (AD E18 SHOULD 2)
            }

            if (s.Glyph.enabled)
            {
                s.GlyphRect.anchoredPosition = new Vector2(at.x - GlyphOffset(ref s) * pop, at.y);
                s.GlyphRect.localScale = s.TextRect.localScale;
            }

            SetAlpha(ref s, s.Alpha);
        }

        /// <summary>
        /// E18b (8-R·A SHOULD-2): a status stamp reports its screen rect (px, bottom-left origin) for its enemy every frame
        /// (target = ZombieEnemy.GetInstanceID(), the number's TargetId); a number on that enemy sits above it. A report
        /// not renewed lapses after one frame; with no number layer yet the call does nothing. No allocation.
        /// </summary>
        public static void ReportAvoidRect(int targetId, Rect screenRect)
        {
            if (s_instance == null || targetId == 0)
            {
                return;
            }

            AvoidRect[] avoid = s_instance._avoid;
            int frame = Time.frameCount, pick = -1;
            for (int i = 0; i < avoid.Length; i++)
            {
                if (avoid[i].TargetId == targetId) { pick = i; break; }
                if (pick < 0 && avoid[i].Frame < frame - 1) pick = i;
            }

            if (pick < 0)
            {
                return;   // 64 live stamps: the rest go unavoided this frame
            }

            avoid[pick].TargetId = targetId;
            avoid[pick].Screen = screenRect;
            avoid[pick].Frame = frame;
        }

        private Vector3 KeepOffStamp(int targetId, Vector3 screen, float halfHeight)
        {
            if (targetId == 0)
            {
                return screen;
            }

            int frame = Time.frameCount;
            for (int i = 0; i < _avoid.Length; i++)
            {
                if (_avoid[i].TargetId != targetId || _avoid[i].Frame < frame - 1)
                {
                    continue;
                }

                Rect r = _avoid[i].Screen;
                float floor = r.yMax + AvoidGap + halfHeight;
                if (screen.y < floor && screen.x >= r.xMin - halfHeight * 2f && screen.x <= r.xMax + halfHeight * 2f)
                {
                    screen.y = floor;   // upward only, like the rise
                }

                break;
            }

            return screen;
        }

        /// <summary>Tests: the live number's canvas position (as <see cref="TryReadPlacement"/>) and whether its glyph shows, which.</summary>
        public bool TryReadGlyph(int targetId, out UiWorldNumberGlyph glyph, out bool shown)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].State == SlotState.Live && _slots[i].TargetId == targetId)
                {
                    glyph = _slots[i].Style.Glyph;
                    shown = _slots[i].Glyph.enabled && _slots[i].Glyph.sprite == _glyphs[(int)glyph];
                    return true;
                }
            }

            glyph = UiWorldNumberGlyph.None;
            shown = false;
            return false;
        }

        // Left of the number: half the digits (Oswald ≈ 0.56 em each, bold ≈ 0.64) + the gap + half the glyph.
        private static float GlyphOffset(ref Slot s)
        {
            float size = PointSize(s.Style.Size);
            int digits = s.Assigned != null ? s.Assigned.Length : 2;
            return digits * size * (s.Style.Bold ? 0.32f : 0.28f) + GlyphGap + size * GlyphScaleOf(s.Style.Size) * 0.5f;
        }

        private static float GlyphScaleOf(UiWorldNumberSize size) => size <= UiWorldNumberSize.M ? GlyphScaleSmall : GlyphScale;

        // DEC-077: a number never covers the player. Inside the player's screen box (feet → head, ± half a body) it
        // moves up above the head — upward only, like the rise.
        private static Vector3 KeepOffPlayer(Camera cam, Vector3 screen)
        {
            PlayerRig rig = PlayerRegistry.Primary;
            if (rig == null)
            {
                return screen;
            }

            Vector3 feet = rig.transform.position;
            Vector3 f = cam.WorldToScreenPoint(feet);
            Vector3 h = cam.WorldToScreenPoint(feet + Vector3.up * 1.9f);
            Vector3 r = cam.WorldToScreenPoint(feet + cam.transform.right * 0.45f);
            if (f.z <= 0f || h.z <= 0f)
            {
                return screen;
            }

            float half = Mathf.Abs(r.x - f.x);
            float bottom = Mathf.Min(f.y, h.y), top = Mathf.Max(f.y, h.y);
            if (Mathf.Abs(screen.x - f.x) <= half && screen.y >= bottom && screen.y <= top + 8f)
            {
                screen.y = top + 8f;
            }

            return screen;
        }

        // ------------------------------------------------------------------ slots

        private void ApplyStyle(ref Slot s)
        {
            int rounded = Mathf.Max(1, Mathf.RoundToInt(s.Amount));
            string text = NumberText(rounded);
            if (!ReferenceEquals(s.Assigned, text))
            {
                s.Text.text = text;
                s.Assigned = text;
            }

            float size = PointSize(s.Style.Size);
            if (!Mathf.Approximately(s.Text.fontSize, size)) s.Text.fontSize = size;
            FontStyles weight = s.Style.Bold ? FontStyles.Bold : FontStyles.Normal;
            if (s.Text.fontStyle != weight) s.Text.fontStyle = weight;
            Color c = UiTokens.Get(s.Style.Color);
            if (s.Text.color != c) s.Text.color = c;

            bool burst = s.Style.Burst && s.Burst.sprite != null;
            if (s.Burst.enabled != burst) s.Burst.enabled = burst;
            if (burst)
            {
                float d = size * BurstScale;
                s.BurstRect.sizeDelta = new Vector2(d, d);
                // A cut shape, not a stamped sticker (AD E18 SHOULD 1): ±18° per number, from its sequence (deterministic).
                float turn = ((s.Sequence * 2654435761u) >> 16 & 0xFFFF) / 65535f * 36f - 18f;
                s.BurstRect.localRotation = Quaternion.Euler(0f, 0f, turn);
            }

            Sprite glyph = _glyphs[(int)s.Style.Glyph];
            bool showGlyph = glyph != null;
            if (s.Glyph.enabled != showGlyph) s.Glyph.enabled = showGlyph;
            if (showGlyph)
            {
                if (s.Glyph.sprite != glyph) s.Glyph.sprite = glyph;
                if (s.Glyph.color != c) s.Glyph.color = c;
                float g = size * GlyphScaleOf(s.Style.Size);
                s.GlyphRect.sizeDelta = new Vector2(g, g);
            }
        }

        private static void SetAlpha(ref Slot s, float a)
        {
            s.TextRenderer.SetAlpha(a);
            if (s.Burst.enabled) s.BurstRenderer.SetAlpha(a);
            if (s.Glyph.enabled) s.GlyphRenderer.SetAlpha(a);
        }

        private static void Release(ref Slot s)
        {
            s.State = SlotState.Free;
            s.TargetId = 0;
            Hide(ref s);
        }

        private static void Hide(ref Slot s)
        {
            s.Alpha = 0f;
            s.TextRenderer.SetAlpha(0f);
            s.BurstRenderer.SetAlpha(0f);
            s.GlyphRenderer.SetAlpha(0f);
        }

        private void OnPhaseChanged(RunPhase phase)
        {
            if (phase == RunPhase.RoundComplete || phase == RunPhase.Dead)
            {
                Flush();
            }
        }

        private void OnSceneChanged(Scene from, Scene to)
        {
            Clear();
        }

        // ------------------------------------------------------------------ text / art

        // WP-062: bounded table filled at type load (no string in a hit frame, no unbounded cache).
        private const int NumberTableSize = 10000;
        private static readonly string[] s_numberTable = BuildNumberTable();
        private const string OverflowNumberText = "9999+";

        private static string[] BuildNumberTable()
        {
            var table = new string[NumberTableSize];
            for (int i = 0; i < table.Length; i++) table[i] = i.ToString();
            return table;
        }

        /// <summary>Integer text without hit-path allocations or an unbounded cache.</summary>
        public static string NumberText(int value)
        {
            return value >= 0 && value < s_numberTable.Length ? s_numberTable[value] : OverflowNumberText;
        }

        /// <summary>
        /// The house FaceOutline on the numeric face (AP-012 / UiFontBuilder "- Outline" preset, tuned for the world sizes):
        /// a ~1 px InkStroke outline outside the face at M 22 (Oswald atlas 64 pt, gradient scale 13 → width 0.3, face dilate 0.3) and a 2 px InkStroke
        /// shadow down-right (underlay offset 0.32 each way). Outline ≥ 4.5:1 against any world pixel: the cream face sits on
        /// its own ink edge.
        /// </summary>
        private static Material NumberMaterial(TMP_FontAsset font)
        {
            if (s_material != null)
            {
                return s_material;
            }

            if (font == null || font.material == null)
            {
                return null;
            }

            Color ink = UiTokens.Get(UiColor.InkStroke);
            s_material = new Material(font.material) { name = "DD_WorldNumber (FaceOutline)", hideFlags = HideFlags.DontSave };
            s_material.EnableKeyword(ShaderUtilities.Keyword_Outline);
            s_material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            // TMP centres the outline on the glyph edge; the face is dilated by the same amount so the ink sits OUTSIDE
            // the Oswald stroke (at 22 px an undilated 0.45 left a 1 px face — the digits read grey).
            s_material.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.3f);
            s_material.SetFloat(ShaderUtilities.ID_FaceDilate, 0.3f);
            // A kill is carried by weight alone at M 22 (AD E18 MUST 1: bold stem ≥ 1.3× regular): TMP bold = +_WeightBold / 4
            // dilation on the same material (one batch); the font's 0.75 was indistinguishable at 22 px.
            s_material.SetFloat(ShaderUtilities.ID_WeightBold, 1.5f);
            s_material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(ink.r, ink.g, ink.b, 1f));
            s_material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(ink.r, ink.g, ink.b, 0.7f));
            s_material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.32f);
            s_material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.32f);
            s_material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.1f);
            return s_material;
        }

        /// <summary>The crit starburst (a shape, not a colour — AD): 10 points, ink body, 2 px cream rim, built once from the tokens.</summary>
        private static Sprite BurstSprite()
        {
            if (s_burst != null)
            {
                return s_burst;
            }

            const int size = 128;
            const int points = 10;
            Color ink = UiTokens.Get(UiColor.InkStroke);
            Color rim = UiTokens.Get(UiColor.TextPrimary);
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "DD_WorldNumberBurst", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / c;
                    float a = Mathf.Atan2(dy, dx) * points / (2f * Mathf.PI);
                    float tri = Mathf.Abs(a - Mathf.Floor(a) - 0.5f) * 2f;   // 1 at a point, 0 between
                    float edge = Mathf.Lerp(0.62f, 0.98f, tri);             // star radius at this angle
                    float d = (edge - r) * c;                               // px inside the edge
                    float alpha = Mathf.Clamp01(d + 0.5f);
                    Color col = d < 2.5f ? rim : ink;
                    col.a = alpha * (d < 2.5f ? 1f : 0.92f);
                    px[y * size + x] = col;
                }
            }

            tex.SetPixels32(px);
            tex.Apply(false, true);
            s_burst = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            s_burst.name = "DD_WorldNumberBurst";
            return s_burst;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            CameraOverride = null;
        }
    }
}
