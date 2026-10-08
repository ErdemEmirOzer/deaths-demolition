using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-098A (C-00 08.10; HAREKET-MENU M7 / M8, AP-034 M1) · the ink wave behind a lifted control: the AP-034 ripple mask in
    /// cream (TextPrimary α <see cref="UiMenuMotionTokens.InkWaveAlpha"/>, under the hover wash's 0.08 - contrast-safe), Ø 56 → 96
    /// over Emphasis 400 (Sweep) while it fades out. The control's first child, so it draws behind the glyph and the word.
    /// Rules (§2 M7): a focus held under <see cref="UiMenuMotionTokens.HoverDwellGate"/> (80 ms) starts nothing (the envelope
    /// waits out the gate, and an exit inside it cancels); one wave per control per <see cref="UiMenuMotionTokens.InkWaveMinInterval"/>;
    /// at most <see cref="UiMenuMotionTokens.MaxHoverTraces"/> waves at once across the UI; none under Reduced Motion (the kit's
    /// focus brackets and wash carry the state) or during a hero choreography (<see cref="UiIconMotion.HeroActive"/>).
    /// No Update: two struct tweens on the motion core (the group's fade, the rect's scale) per wave; built once per control.
    ///
    /// USAGE: <see cref="Attach"/> at build; the control's LiftChanged calls <see cref="UiPresets.InkWave"/>.
    /// DON'T: tint it, or raise its alpha past the wash (a wave is texture, never a state).
    /// ACCESSIBILITY: decoration only (≤ 0.06 cream); absent under Reduced Motion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiInkWave : MonoBehaviour
    {
        public const string ObjectName = "InkWave";

        private static readonly float[] s_recent = new float[UiMenuMotionTokens.MaxHoverTraces];
        private static int s_recentHead;

        private CanvasGroup _group;
        private RectTransform _rect;
        private float _startedAt = -10f;

        /// <summary>The last wave's start (tests: rate limit and gate).</summary>
        public float StartedAt
        {
            get { return _startedAt; }
        }

        public CanvasGroup Group
        {
            get { return _group; }
        }

        /// <summary>The wave behind <paramref name="control"/> (created once; a second call returns it).</summary>
        public static UiInkWave Attach(RectTransform control)
        {
            if (control == null)
            {
                return null;
            }

            Transform existing = control.Find(ObjectName);
            if (existing != null)
            {
                return existing.GetComponent<UiInkWave>();
            }

            Image image = UiFactory.NewImage(ObjectName, control, UiColor.TextPrimary, UiBrushArt.Get(UiBrushSprite.InkRipple));
            image.raycastTarget = false;
            image.preserveAspect = true;
            Color cream = image.color;
            cream.a = UiMenuMotionTokens.InkWaveAlpha;
            image.color = cream;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(UiMenuMotionTokens.InkWaveTo, UiMenuMotionTokens.InkWaveTo);
            rect.anchoredPosition = Vector2.zero;
            rect.SetAsFirstSibling();
            var wave = image.gameObject.AddComponent<UiInkWave>();
            wave._rect = rect;
            wave._group = UiFactory.Group(image.gameObject);
            wave._group.alpha = 0f;
            wave._group.blocksRaycasts = false;
            wave._group.interactable = false;
            return wave;
        }

        /// <summary>The control lifted (true) or settled (false). True when a wave was started.</summary>
        public bool Lift(bool lifted)
        {
            float now = UiMotion.Now(UiClock.Unscaled);
            float gate = UiMenuMotionTokens.HoverDwellGate;
            if (!lifted)
            {
                if (now < _startedAt + gate)
                {
                    Cancel();   // left inside the dwell gate: the wave never showed
                }

                return false;
            }

            if (UiMotion.ReducedMotion || UiIconMotion.HeroActive || UiCurtain.IsCovering || !gameObject.activeInHierarchy)
            {
                return false;
            }

            if (now - _startedAt < UiMenuMotionTokens.InkWaveMinInterval || !TakeSlot(now))
            {
                return false;
            }

            _startedAt = now;
            float seconds = UiMotionTokens.Seconds(UiDuration.Emphasis);
            _group.alpha = 0f;
            _rect.localScale = new Vector3(UiMenuMotionTokens.InkWaveFrom / UiMenuMotionTokens.InkWaveTo, UiMenuMotionTokens.InkWaveFrom / UiMenuMotionTokens.InkWaveTo, 1f);
            UiMotion.AlphaSeconds(_group, 1f, 0f, seconds, UiEase.Sweep, UiMotionClass.Decor, gate);
            UiMotion.ScaleSeconds(_rect, UiMenuMotionTokens.InkWaveFrom / UiMenuMotionTokens.InkWaveTo, 1f, seconds, UiEase.Sweep, UiMotionClass.Decor, gate);
            return true;
        }

        private void Cancel()
        {
            UiMotion.KillTarget(_group, false);
            UiMotion.KillTarget(_rect, false);
            _group.alpha = 0f;
            _startedAt = -10f;
            ReleaseSlot();
        }

        /// <summary>The UI-wide cap: a wave takes a slot for Emphasis + the gate; with every slot busy no new wave starts.</summary>
        private static bool TakeSlot(float now)
        {
            float busy = UiMotionTokens.Seconds(UiDuration.Emphasis) + UiMenuMotionTokens.HoverDwellGate;
            for (int i = 0; i < s_recent.Length; i++)
            {
                int k = (s_recentHead + i) % s_recent.Length;
                if (now - s_recent[k] >= busy || s_recent[k] > now)
                {
                    s_recent[k] = now;
                    s_recentHead = (k + 1) % s_recent.Length;
                    return true;
                }
            }

            return false;
        }

        private static void ReleaseSlot()
        {
            int last = (s_recentHead + s_recent.Length - 1) % s_recent.Length;
            s_recent[last] = -10f;
        }

        /// <summary>Waves running now (tests: never more than MaxHoverTraces).</summary>
        public static int Busy(float now)
        {
            float busy = UiMotionTokens.Seconds(UiDuration.Emphasis) + UiMenuMotionTokens.HoverDwellGate;
            int n = 0;
            for (int i = 0; i < s_recent.Length; i++)
            {
                if (now - s_recent[i] < busy && s_recent[i] <= now) n++;
            }

            return n;
        }

        public static void ResetForTests()
        {
            for (int i = 0; i < s_recent.Length; i++)
            {
                s_recent[i] = -10f;
            }

            s_recentHead = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ResetForTests();
        }
    }

    /// <summary>
    /// WP-098A (C-00 08.10; HAREKET-MENU M10, AP-034 M2) · the click splash: the AP-034 splash mask inked from its centre out
    /// (InkReveal, the Blot time map in wipe mode) over Screen 280, then lifted (Base 220, Exit), with six droplets
    /// (<see cref="UiFxParticles"/> burst: 160 px/s, 400 px/s² gravity, Base 220). Cream Ø 160 α 0.14 on any control; on the
    /// hero (PLAY) vermilion Ø 240 α 0.5 - a large flash (57 600 px² ≥ 43 000) through <see cref="UiMotion.FlashOnset"/>: a
    /// refused onset merges (no second splash). Alphas × FlashScale (Reduce Flashes 0.4). A source plays at most once per
    /// <see cref="UiMenuMotionTokens.SplashMinInterval"/>; the pool is two splashes (the older one is reused). Under Reduced
    /// Motion nothing plays - the bevel / press wash carry the press. One layer per screen, over its content, no raycasts.
    ///
    /// USAGE: <see cref="Create"/> once on the screen root; <see cref="UiPresets.InkSplash"/> on Confirm, at the pointer (mouse)
    /// or the control's centre (pad / keyboard).
    /// DON'T: splash on a press that changes nothing, or use the hero splash for anything but the screen's one primary.
    /// ACCESSIBILITY: Flash class - scaled by Reduce Flashes, gated as a large flash on the hero; absent under Reduced Motion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiInkSplashLayer : MonoBehaviour
    {
        private struct Splash
        {
            public Image Image;
            public UiFx Fx;
            public CanvasGroup Group;
            public UiFxParticles Drops;
            public float StartedAt;
        }

        private readonly Splash[] _pool = new Splash[UiMenuMotionTokens.SplashPool];
        private readonly int[] _sources = new int[UiMenuMotionTokens.SplashPool];
        private readonly float[] _sourceAt = new float[UiMenuMotionTokens.SplashPool];
        private RectTransform _rect;

        /// <summary>Splashes started since the layer was built (tests).</summary>
        public int Played { get; private set; }

        public RectTransform Rect
        {
            get { return _rect; }
        }

        /// <summary>A full-size, raycast-free layer under <paramref name="parent"/> (made the last sibling: over the content).</summary>
        public static UiInkSplashLayer Create(RectTransform parent, string name = "InkSplash")
        {
            RectTransform rect = UiFactory.NewRect(name, parent);
            UiFactory.Fill(rect);
            rect.SetAsLastSibling();
            var layer = rect.gameObject.AddComponent<UiInkSplashLayer>();
            layer._rect = rect;
            for (int i = 0; i < layer._pool.Length; i++)
            {
                layer._pool[i] = layer.Build(i);
                layer._sources[i] = 0;
                layer._sourceAt[i] = -10f;
            }

            return layer;
        }

        private Splash Build(int index)
        {
            Image image = UiFactory.NewImage("Splash" + index, _rect, UiColor.TextPrimary, UiBrushArt.Get(UiBrushSprite.InkSplash));
            image.raycastTarget = false;
            image.preserveAspect = true;
            RectTransform rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var fx = image.gameObject.AddComponent<UiFx>();
            fx.Configure(UiFxMode.InkReveal, UiDuration.Screen, new Vector3(0f, 1f, 0f), UiInkChannel.Blot);
            fx.Hold();
            CanvasGroup group = UiFactory.Group(image.gameObject);
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            RectTransform dropsRect = UiFactory.NewRect("Drops", rect);
            UiFactory.Fill(dropsRect);
            var drops = dropsRect.gameObject.AddComponent<UiFxParticles>();
            drops.Configure(UiMenuMotionTokens.SplashDroplets, UiMenuMotionTokens.SplashDropletSize, UiMenuMotionTokens.SplashDropletSpeed,
                UiMenuMotionTokens.SplashDropletGravity, UiMotionTokens.Seconds(UiDuration.Base));
            return new Splash { Image = image, Fx = fx, Group = group, Drops = drops, StartedAt = -10f };
        }

        /// <summary>
        /// Splash at <paramref name="localPoint"/> (this layer's space) for <paramref name="source"/> (the control's instance id).
        /// True when it played: not under RM, not within the source's interval, not a hero onset the FlashGate merged.
        /// </summary>
        public bool Play(Vector2 localPoint, int source, bool hero)
        {
            if (UiMotion.ReducedMotion || !gameObject.activeInHierarchy)
            {
                return false;
            }

            float now = UiMotion.Now(UiClock.Unscaled);
            for (int i = 0; i < _sources.Length; i++)
            {
                if (_sources[i] == source && now - _sourceAt[i] < UiMenuMotionTokens.SplashMinInterval)
                {
                    return false;
                }
            }

            if (hero && !UiMotion.FlashOnset(true))
            {
                return false;   // a large onset inside the gate's second merges into the one running
            }

            int slot = _pool[0].StartedAt <= _pool[1 % _pool.Length].StartedAt ? 0 : 1 % _pool.Length;
            Splash splash = _pool[slot];
            _sources[slot] = source;
            _sourceAt[slot] = now;
            _pool[slot].StartedAt = now;

            float size = hero ? UiMenuMotionTokens.SplashSizeHero : UiMenuMotionTokens.SplashSize;
            Color ink = UiTokens.Get(hero ? UiColor.AccentPrimary : UiColor.TextPrimary);
            ink.a = (hero ? UiMenuMotionTokens.SplashAlphaHero : UiMenuMotionTokens.SplashAlpha) * UiTokens.FlashScale;
            RectTransform rect = splash.Image.rectTransform;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = localPoint;
            rect.SetAsLastSibling();
            if (splash.Image.color != ink)
            {
                splash.Image.color = ink;
                splash.Drops.color = ink;
            }

            UiMotion.KillTarget(splash.Group, false);
            splash.Group.alpha = 1f;
            splash.Fx.Play();
            splash.Drops.Play();
            float ink0 = UiMotionTokens.Seconds(UiDuration.Screen);
            UiMotion.AlphaSeconds(splash.Group, 1f, 0f, UiMotionTokens.Seconds(UiDuration.Base), UiEase.Exit, UiMotionClass.Decor, ink0);
            Played++;
            return true;
        }

        /// <summary>A point in this layer's space for a screen point (the pointer) or a control's centre.</summary>
        public Vector2 LocalPoint(Vector2 screenPoint, Camera eventCamera)
        {
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rect, screenPoint, eventCamera, out local);
            return local - _rect.rect.center;
        }

        private static readonly Vector3[] s_corners = new Vector3[4];

        /// <summary>The centre of <paramref name="control"/> in this layer's space (pad and keyboard presses splash there).</summary>
        public Vector2 CentreOf(RectTransform control)
        {
            if (control == null)
            {
                return Vector2.zero;
            }

            control.GetWorldCorners(s_corners);
            Vector3 centre = (s_corners[0] + s_corners[2]) * 0.5f;
            Vector2 local = _rect.InverseTransformPoint(centre);
            return local - _rect.rect.center;
        }
    }
}
