using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-098A (KIT-API §2, §4 "Pane"; KIT-KILAVUZU §1 "iz, yüzey değil", §4.2) · the C1 + C2 + C37 frameless pane carrier:
    /// a full-screen ink scrim (C1, on the screen root, just under the pane), an optional soft sumi wash (C2) under a
    /// clipped content column, and the traces the pane inks in when it opens (title stroke, dividers, the edge).
    ///   Open   input on at once (the content group is interactable in the same frame) and focus on the caller's control;
    ///          C1 fades in (Fast), C2 and the tracked marks ink in (Screen 280), the content rises (PaneIn, 60 ms late);
    ///          UiEvent.PaneOpened.
    ///   Close  input off at once; content, C2 and C1 fade (Fast, Exit) and a silent timeline cue deactivates the pane
    ///          (<see cref="UiTimeline.Callback"/>). The caller returns focus to the control that opened it.
    /// WP-098A (HAREKET-MENU §4, C-00 08.10): the content's open / close are <see cref="UiPresets.PaneOpen"/> /
    /// <see cref="UiPresets.PaneClose"/> - the same presets as the main menu's panes (one timing for S-04's windows and the menu).
    /// Under Reduced Motion every part lands at its end state in the same frame. No Update; Open / Close / HandleCancel
    /// allocate nothing once warm (cached event name, a pre-sized mark list, struct tweens).
    ///
    /// USAGE: <see cref="UiBrushKit.Pane"/> → lay rows inside <see cref="Content"/> → <see cref="Open"/>; the screen's Update
    /// reads Cancel (UiInput.CancelPressed) and calls <see cref="HandleCancel"/>.
    /// DON'T: move or resize <see cref="Content"/> (PaneIn animates it), SetActive the pane yourself, put text on a Pane or
    /// Wallpaper scrim without the wash, or fade it by hand (TickFade / MoveTowards).
    /// ACCESSIBILITY: <see cref="TextGroundAlpha"/> - the least combined ink under the content - is ≥ 0.945 for every level
    /// that may carry text (TextSecondary ≥ 4.5:1 on a white world in linear light); a Pane / Wallpaper scrim without its
    /// wash is logged once at build. Focus moves on frame 1; motion is Decor.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiBrushPane : MonoBehaviour, IUiMotionListener
    {
        private const int CloseDone = 1;

        private RectTransform _root;
        private RectTransform _content;
        private CanvasGroup _group;
        private Image _scrim;
        private Image _wash;
        private UiFx _washFx;
        private UiBrushScrim _level;
        private UiBrushMark _edge;
        private readonly List<UiBrushMark> _marks = new List<UiBrushMark>(UiBrushTokens.MaxTraces);
        private readonly List<Image> _washes = new List<Image>(UiBrushTokens.MaxWash);
        private readonly List<UiFx> _washFxs = new List<UiFx>(UiBrushTokens.MaxWash);
        private string _eventName;
        private int _owner;
        private bool _open;

        /// <summary>The pane's rect, named by the caller ("PaneCard").</summary>
        public RectTransform Root
        {
            get { return _root; }
        }

        /// <summary>Clipped; lay rows inside it - never move Content itself (PaneIn animates it).</summary>
        public RectTransform Content
        {
            get { return _content; }
        }

        /// <summary>C1 on the screen root, under the pane (null with <see cref="UiBrushScrim.None"/>).</summary>
        public Image Scrim
        {
            get { return _scrim; }
        }

        /// <summary>C2 under Content (null with <see cref="UiBrushWash.None"/>).</summary>
        public Image Wash
        {
            get { return _wash; }
        }

        public UiBrushScrim ScrimLevel
        {
            get { return _level; }
        }

        public bool IsOpen
        {
            get { return _open; }
        }

        /// <summary>The content column's input group (interactable on the frame Open is called).</summary>
        public CanvasGroup Group
        {
            get { return _group; }
        }

        /// <summary>The least combined ink α under Content (scrim ⊕ wash); tests hold it ≥ <see cref="UiBrushTokens.TextGroundMin"/>.</summary>
        public float TextGroundAlpha
        {
            get { return UiBrushTokens.Combined(UiBrushTokens.Scrim(_level), _wash != null ? UiBrushTokens.WashCore : 0f); }
        }

        /// <summary>Kit-only builder (<see cref="UiBrushKit.Pane"/>).</summary>
        internal static UiBrushPane Build(RectTransform screenRoot, RectTransform host, string name, UiBrushScrim scrim, UiBrushWash wash)
        {
            RectTransform root = UiFactory.NewRect(name, host);
            UiFactory.Fill(root);
            var pane = root.gameObject.AddComponent<UiBrushPane>();
            pane._root = root;
            pane._level = scrim;
            pane._eventName = name;
            pane._owner = pane.GetInstanceID();

            if (scrim != UiBrushScrim.None && screenRoot != null)
            {
                pane._scrim = UiFactory.NewImage(name + ".Scrim", screenRoot, UiColor.SurfaceInset, UiSprites.Solid);
                Color ink = pane._scrim.color;
                ink.a = UiBrushTokens.Scrim(scrim);
                pane._scrim.color = ink;
                UiFactory.Fill(pane._scrim.rectTransform);
                // A pane over the menu leaves the rail clickable; a window / wallpaper / confirm takes every click under it.
                pane._scrim.raycastTarget = scrim != UiBrushScrim.Pane;
                pane._scrim.transform.SetSiblingIndex(SiblingUnder(screenRoot, root));
                pane._scrim.gameObject.SetActive(false);
            }

            if (wash == UiBrushWash.Column)
            {
                pane._wash = pane.AddWash(root, null);
            }
            else if (scrim == UiBrushScrim.Pane || scrim == UiBrushScrim.Wallpaper)
            {
                Debug.LogWarning("[UiBrushKit] " + name + ": a " + scrim + " scrim without the C2 wash - text on it is " +
                                 UiBrushTokens.Scrim(scrim).ToString("0.00") + " ink, under the " + UiBrushTokens.TextGroundMin.ToString("0.000") +
                                 " floor (KIT-KILAVUZU §1); lay text in a wash (UiBrushKit.Wash) or pass UiBrushWash.Column", root);
            }

            pane._content = UiFactory.NewRect("Content", root);
            UiFactory.Fill(pane._content);
            UiFactory.ClipContent(pane._content);
            pane._group = UiFactory.Group(pane._content.gameObject);

            // Closed until Open: the root stays active while the caller builds (TMP measures only active text), but draws and
            // takes nothing - content invisible and inert, the washes clear; Close's cue deactivates it from then on.
            pane._group.alpha = 0f;
            pane._group.interactable = false;
            pane._group.blocksRaycasts = false;
            for (int i = 0; i < pane._washes.Count; i++)
            {
                pane._washes[i].canvasRenderer.SetAlpha(0f);
            }

            return pane;
        }

        /// <summary>
        /// A C2 for <paramref name="column"/> (null = the whole pane): a child of the root, before Content (outside its clip),
        /// spilling <see cref="UiBrushTokens.WashReach"/> past the column. AP-034's masks are sliceless and stretch; the
        /// SoftShadow fallback is 9-sliced so its falloff spans exactly the reach and its full core covers the column.
        /// </summary>
        internal Image AddWash(RectTransform root, RectTransform column)
        {
            if (_washes.Count >= UiBrushTokens.MaxWash)
            {
                Debug.LogWarning("[UiBrushKit] " + _eventName + ": a third C2 wash - the window budget is " + UiBrushTokens.MaxWash, root);
            }

            Vector2 size = root.rect.size;
            Image wash = UiFactory.NewImage(_washes.Count == 0 ? "Wash" : "Wash." + _washes.Count, root, UiColor.SurfaceInset, UiSprites.Solid);
            RectTransform rect = wash.rectTransform;
            if (column == null)
            {
                UiFactory.Outset(rect, UiBrushTokens.WashReach);
                size += new Vector2(UiBrushTokens.WashReach * 2f, UiBrushTokens.WashReach * 2f);
            }
            else
            {
                // The column's rect in the root's space, from its corners (build time: the column is laid out by now).
                var corners = new Vector3[4];
                column.GetWorldCorners(corners);
                Vector2 min = root.InverseTransformPoint(corners[0]);
                Vector2 max = root.InverseTransformPoint(corners[2]);
                Rect space = root.rect;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                size = new Vector2(Mathf.Abs(max.x - min.x) + UiBrushTokens.WashReach * 2f, Mathf.Abs(max.y - min.y) + UiBrushTokens.WashReach * 2f);
                rect.sizeDelta = size;
                rect.anchoredPosition = new Vector2(Mathf.Min(min.x, max.x) - UiBrushTokens.WashReach - space.xMin,
                    Mathf.Max(min.y, max.y) + UiBrushTokens.WashReach - space.yMax);
            }

            Sprite sprite = UiBrushArt.Wash(size);
            wash.sprite = sprite;
            if (sprite.border != Vector4.zero)
            {
                wash.type = Image.Type.Sliced;
                wash.pixelsPerUnitMultiplier = Mathf.Max(0.01f, (sprite.border.x - 1f) / UiBrushTokens.WashReach);
            }
            else
            {
                wash.type = Image.Type.Simple;
            }

            Color ink = wash.color;
            ink.a = UiBrushTokens.WashCore;
            wash.color = ink;
            wash.raycastTarget = false;
            if (_content != null)
            {
                wash.transform.SetSiblingIndex(_content.GetSiblingIndex());
            }

            var fx = wash.gameObject.AddComponent<UiFx>();
            fx.Configure(UiFxMode.InkReveal, UiBrushTokens.Open, new Vector3(0f, 1f, 0f), UiBrushArt.RevealChannel(UiBrushSprite.WashSquare));
            _washes.Add(wash);
            _washFxs.Add(fx);
            if (_wash == null)
            {
                _wash = wash;
                _washFx = fx;
            }

            if (_content != null && !_open)
            {
                wash.canvasRenderer.SetAlpha(0f);
            }

            return wash;
        }

        /// <summary>C37: the pane's one edge (kit-only, <see cref="UiBrushKit.EdgeTrace"/>), fitted to the root's height on every Open.</summary>
        internal void SetEdge(UiBrushMark edge)
        {
            _edge = edge;
            FitEdge();
            Track(edge);
        }

        /// <summary>
        /// Ink <paramref name="mark"/> in with Open (Title / Divider / EdgeTrace register themselves; a Tabs / Segments C8 is the
        /// caller's to track). On a closed pane it hides until Open; on an open pane it stays as it is - drawn (a rebuild in
        /// place: language, profile, page). Marks destroyed with an earlier build are pruned here; a second Track is ignored.
        /// </summary>
        public void Track(UiBrushMark mark)
        {
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                if (_marks[i] == null)
                {
                    _marks.RemoveAt(i);
                }
            }

            if (mark == null || _marks.Contains(mark))
            {
                return;
            }

            if (_marks.Count >= UiBrushTokens.MaxTraces)
            {
                Debug.LogWarning("[UiBrushKit] " + _eventName + ": more than " + UiBrushTokens.MaxTraces + " brush traces in one window (KIT-KILAVUZU §1)", this);
            }

            _marks.Add(mark);
            if (!_open)
            {
                mark.Hide(false);
            }
        }

        /// <summary>Open: input at once, focus <paramref name="focus"/>; C1 Fast, C2 + tracked marks ink in (Open), Content PaneIn; UiEvent.PaneOpened.</summary>
        public void Open(Selectable focus)
        {
            UiMotion.KillOwner(_owner, true);   // a close mid-fade lands, and its deactivation cue is dropped
            _open = true;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            _group.interactable = true;
            _group.blocksRaycasts = true;

            if (_scrim != null)
            {
                _scrim.gameObject.SetActive(true);
                _scrim.canvasRenderer.SetAlpha(0f);
                UiMotion.TintAlpha(_scrim, 1f, UiBrushTokens.ScrimIn, UiEase.Settle, UiMotionClass.Decor, 0f, UiClock.Unscaled, _owner);
            }

            for (int i = 0; i < _washes.Count; i++)
            {
                _washes[i].canvasRenderer.SetAlpha(1f);
                _washFxs[i].Play();
            }

            FitEdge();
            for (int i = 0; i < _marks.Count; i++)
            {
                if (_marks[i] != null)
                {
                    _marks[i].Draw(UiBrushTokens.Open);
                }
            }

            UiPresets.PaneOpen(null, null, _group, _content, _owner);   // WP-098A (HAREKET-MENU §4): the menu's pane preset, shared
            UiInput.Focus(focus);
            UiEvents.Raise(UiEvent.PaneOpened, _eventName);
        }

        /// <summary>Close: input off at once; Content, C2, C1 fade (Close, Exit); deactivated by a silent timeline cue. The caller returns focus.</summary>
        public void Close()
        {
            if (!_open)
            {
                return;
            }

            _open = false;
            _group.interactable = false;
            _group.blocksRaycasts = false;
            UiMotion.KillOwner(_owner, true);   // an opening mid-flight lands first (the content back at its rest)

            UiPresets.PaneClose(null, null, _group, _owner);   // WP-098A: the content leaves on the shared close (Fast, Exit)
            if (_scrim != null)
            {
                UiMotion.TintAlpha(_scrim, 0f, UiBrushTokens.Close, UiEase.Exit, UiMotionClass.Decor, 0f, UiClock.Unscaled, _owner);
            }

            for (int i = 0; i < _washes.Count; i++)
            {
                UiMotion.TintAlpha(_washes[i], 0f, UiBrushTokens.Close, UiEase.Exit, UiMotionClass.Decor, 0f, UiClock.Unscaled, _owner);
            }

            for (int i = 0; i < _marks.Count; i++)
            {
                if (_marks[i] != null)
                {
                    _marks[i].Hide(true);
                }
            }

            if (UiMotion.ReducedMotion)
            {
                Deactivate();
                return;
            }

            UiTimeline timeline = UiMotion.Timeline(_owner, this);
            if (!timeline.IsValid)
            {
                Deactivate();   // no free timeline slot: the fades still land, the pane goes now
                return;
            }

            timeline.Callback(UiMotionTokens.Seconds(UiBrushTokens.Close), CloseDone);
        }

        /// <summary>Cancel for an open pane: Close + the Back sound. False when already closed (the screen handles its own Cancel).</summary>
        public bool HandleCancel()
        {
            if (!_open)
            {
                return false;
            }

            Close();
            UiSound.Play(UiSoundKind.Back);
            return true;
        }

        void IUiMotionListener.OnCue(int cueId)
        {
            if (cueId == CloseDone && !_open)
            {
                Deactivate();
            }
        }

        private void Deactivate()
        {
            if (_scrim != null)
            {
                _scrim.gameObject.SetActive(false);
            }

            gameObject.SetActive(false);   // the root: C2 and the content go with it
        }

        private void FitEdge()
        {
            if (_edge == null || _root == null)
            {
                return;
            }

            float length = _root.rect.height;
            var size = new Vector2(length, UiBrushTokens.EdgeWidth);
            if (length > 1f && _edge.Rect.sizeDelta != size)
            {
                _edge.Rect.sizeDelta = size;   // only on a real change: a resize dirties the layout
            }
        }

        /// <summary>The sibling index of <paramref name="node"/>'s top-most ancestor under <paramref name="screenRoot"/> - the scrim goes there, just under it.</summary>
        private static int SiblingUnder(RectTransform screenRoot, Transform node)
        {
            Transform t = node;
            while (t != null && t.parent != screenRoot)
            {
                t = t.parent;
            }

            return t != null ? t.GetSiblingIndex() : 0;
        }

        private void OnDestroy()
        {
            UiMotion.KillOwner(_owner, false);

            // The scrim lives on the screen root, outside the pane: a pane rebuilt with its host (the menu's panes) takes its C1 along.
            if (_scrim != null && Application.isPlaying)
            {
                Destroy(_scrim.gameObject);
            }
        }
    }
}
