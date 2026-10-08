using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// AP-035 main-menu overlay, started only by MainMenuScreen's arrival hook. Plays once per session (at most 2 s),
    /// reports completion once and reads no input. The menu owns skipping and keeps its existing same-frame actions.
    /// The scrim is visual only; title opacity follows the logo until the menu callback reveals its clipped title.
    /// Off in batch unless forced, FastTransitions, and Reduced Motion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiLogoMenuIntro : MonoBehaviour
    {
        /// <summary>Above the menu screens and their overlays / modals, under the curtain (400).</summary>
        public const int SortingOrder = UiLayer.Curtain - 10;

        /// <summary>The logo's width on the menu (spec 960: the canonical frame's own size at 1080p).</summary>
        public const float MenuWidth = 760f;

        /// <summary>The ink scrim under the logo, × the logo's alpha.</summary>
        public const float ScrimAlpha = 0.72f;

        /// <summary>Tests: allow it under batch mode.</summary>
        public static bool ForceInBatch;

        private static bool s_played;

        private Canvas _canvas;
        private Image _scrim;
        private UiLogoAnimator _logo;
        private CanvasGroup _titleGroup;
        private bool _titleGroupAdded;
        private System.Action _onDone;
        private bool _running;

        /// <summary>Whether the intro may play here (batch, FastTransitions, reduced motion).</summary>
        public static bool Allowed
        {
            get { return (!Application.isBatchMode || ForceInBatch) && !PrototypeUiFlow.FastTransitions && !UiMotion.ReducedMotion; }
        }

        /// <summary>True once this session's intro has started (it plays once).</summary>
        public static bool PlayedThisSession => s_played;

        /// <summary>The live overlay (tests); null when none runs.</summary>
        public static UiLogoMenuIntro Current { get; private set; }

        public bool Running => _running;
        public UiLogoAnimator Logo => _logo;

        /// <summary>The menu title's current opacity factor (tests: never ≥ 0.95 while the logo is ≥ 0.95).</summary>
        public float TitleAlpha => _titleGroup != null ? _titleGroup.alpha : 1f;

        /// <summary>The menu owns timing and input; calls this once from its revealed arrival hook.</summary>
        public void Play(System.Action onDone = null)
        {
            if (s_played || !Allowed)
            {
                onDone?.Invoke();
                return;
            }

            _onDone = onDone;
            Begin();
        }

        private void Begin()
        {
            s_played = true;
            Current = this;
            _running = true;
            _canvas = UiFactory.CreateCanvas("UiLogoMenuIntro.Canvas", SortingOrder, transform);
            var root = (RectTransform)_canvas.transform;
            _scrim = UiFactory.NewImage("Scrim", root, UiColor.SurfaceInset, UiSprites.Solid);
            UiFactory.Fill((RectTransform)_scrim.transform);
            _scrim.raycastTarget = false;   // the menu owns the skipping press and its same-frame action
            _scrim.color = UiTokens.Get(UiColor.SurfaceInset);
            _scrim.canvasRenderer.SetAlpha(ScrimAlpha);

            _logo = UiLogoAnimator.Create(root, MenuWidth);
            float height = UiLogoAnimator.CompositionHeight * MenuWidth / UiLogoAnimator.CompositionWidth;
            _logo.Root.anchoredPosition = new Vector2(0f, -height * 0.5f);
            _logo.Impact += OnImpact;

            TMP_Text title = FindTitle();
            if (title != null)
            {
                _titleGroup = title.GetComponent<CanvasGroup>();
                _titleGroupAdded = _titleGroup == null;
                if (_titleGroupAdded)
                {
                    _titleGroup = title.gameObject.AddComponent<CanvasGroup>();
                }

                _titleGroup.alpha = 0f;
            }

            _logo.PlayMenuIntro(OnDone);
            Follow();
        }

        /// <summary>The menu's title (MainMenuScreen's "Title" label), found without touching the menu's code.</summary>
        private static TMP_Text FindTitle()
        {
            MainMenuScreen menu = FindObjectOfType<MainMenuScreen>();
            if (menu == null)
            {
                return null;
            }

            TMP_Text[] texts = menu.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].name == "Title")
                {
                    return texts[i];
                }
            }

            return null;
        }

        private static void OnImpact()
        {
            UiEvents.Raise(UiEvent.BootLogo);   // K12: the boot seal's rin bell, no new UiEvent
        }

        /// <summary>Tests: the skip a press would make.</summary>
        public void RequestSkip()
        {
            if (_running && _logo != null)
            {
                _logo.Skip();
                Follow();
            }
        }

        private void Update()
        {
            if (_running)
            {
                Follow();
            }
        }

        /// <summary>The scrim and the title follow the logo's opacity (title = its complement).</summary>
        private void Follow()
        {
            if (_logo == null)
            {
                return;
            }

            float a = _logo.Alpha;
            if (_scrim != null)
            {
                _scrim.canvasRenderer.SetAlpha(ScrimAlpha * a);
            }

            if (_titleGroup != null)
            {
                _titleGroup.alpha = 1f - a;
            }
        }

        private void OnDone()
        {
            _running = false;
            if (_titleGroup != null)
            {
                _titleGroup.alpha = 1f;
                if (_titleGroupAdded)
                {
                    Destroy(_titleGroup);
                }

                _titleGroup = null;
            }

            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
            }

            _logo = null;
            _scrim = null;
            if (Current == this)
            {
                Current = null;
            }

            System.Action done = _onDone;
            _onDone = null;
            done?.Invoke();   // one owner, one completion, after the logo and temporary title group are gone
        }

        private void OnDestroy()
        {
            // Scene teardown cancels the hook; never reveal a title on a destroyed menu.
            _onDone = null;
            if (_titleGroup != null)
            {
                _titleGroup.alpha = 1f;
                if (_titleGroupAdded) Destroy(_titleGroup);
            }

            if (Current == this) Current = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_played = false;
            ForceInBatch = false;
            Current = null;
        }

        /// <summary>Tests: a new "session" for the once-per-session rule.</summary>
        public static void ResetSessionForTests()
        {
            s_played = false;
        }
    }
}
