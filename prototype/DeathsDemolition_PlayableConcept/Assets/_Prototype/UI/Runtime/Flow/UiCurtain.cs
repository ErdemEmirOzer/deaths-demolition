using System;
using System.Collections;
using DD.Prototype.Core.Boot;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 slice 10 · the loading curtain (UI v3 direction §15). One overlay canvas on the
    /// flow runner (UiLayer.Curtain 400, survives scene loads) with two depths:
    ///   CURTAIN - two lacquer doors meet in the middle and part again (fast loads);
    ///   SCREEN  - the doors plus the loading group: the ensō progress ring (honest 0 → 0.9,
    ///             Data class, never eased), LOADING / ENTERING and the destination label.
    /// <see cref="PrototypeUiFlow"/> drives it around SceneManager.LoadSceneAsync; screens
    /// that open behind it start their intro on <see cref="Revealed"/> instead of Start().
    /// Slice 10a is the flow core with plain doors and ring; the Look B plate, InkReveal
    /// ring stroke (DD/UI/Fx, S-07), seal, tip strip and error modal follow in 10b.
    /// </summary>
    public sealed class UiCurtain : MonoBehaviour
    {
        /// <summary>Ring diameter and its centre offset above the screen centre (direction §15.3).</summary>
        public const float RingSize = 240f;
        public const float RingLift = 40f;

        /// <summary>0.9 of the fill is the load; the last 36° close on activation.</summary>
        public const float LoadShare = 0.9f;

        private static UiCurtain s_instance;
        private static bool s_covering;

        private Canvas _canvas;
        private RectTransform _root;
        private RectTransform _leftDoor;
        private RectTransform _rightDoor;
        private CanvasGroup _group;
        private Image _ring;
        private TMP_Text _eyebrow;
        private TMP_Text _destination;
        private TMP_Text _subline;
        private float _shownFill = -1f;
        private bool _cursorWasVisible = true;

        /// <summary>Raised when the doors are half open over the new scene (screens start their intro here).</summary>
        public static event Action Revealed;

        /// <summary>True from the moment the doors start closing until they are half open again.</summary>
        public static bool IsCovering
        {
            get { return s_covering; }
        }

        /// <summary>Yield until the curtain no longer covers the screen (immediately when it never did).</summary>
        public static IEnumerator WaitRevealed()
        {
            while (s_covering)
            {
                yield return null;
            }
        }

        public static UiCurtain Instance
        {
            get
            {
                if (s_instance == null)
                {
                    var host = new GameObject("UiCurtain") { hideFlags = HideFlags.HideAndDontSave };
                    DontDestroyOnLoad(host);
                    s_instance = host.AddComponent<UiCurtain>();
                    s_instance.Build();
                }

                return s_instance;
            }
        }

        /// <summary>The group (ring + labels) is visible: SCREEN depth.</summary>
        public bool GroupVisible
        {
            get { return _group != null && _group.gameObject.activeSelf; }
        }

        /// <summary>The fill the ring currently shows (0..1).</summary>
        public float RingFill
        {
            get { return _ring != null ? _ring.fillAmount : 0f; }
        }

        public string EyebrowText
        {
            get { return _eyebrow != null ? _eyebrow.text : string.Empty; }
        }

        // ================================================================== build

        private void Build()
        {
            _canvas = UiFactory.CreateCanvas("UiCurtain.Canvas", UiLayer.Curtain, transform);
            _root = (RectTransform)_canvas.transform;

            _leftDoor = Door("DoorLeft", 0f, 0.5f);
            _rightDoor = Door("DoorRight", 0.5f, 1f);

            var groupRect = UiFactory.NewRect("Group", _root);
            UiFactory.Fill(groupRect);
            _group = UiFactory.Group(groupRect.gameObject);

            Image ghost = UiFactory.NewImage("RingGhost", groupRect, UiColor.TextPrimary, UiSprites.RingThin);
            Place((RectTransform)ghost.transform, RingSize);
            ghost.color = WithAlpha(UiTokens.Get(UiColor.TextPrimary), 0.12f);

            _ring = UiFactory.NewImage("Ring", groupRect, UiColor.TextPrimary, UiSprites.Ring);
            Place((RectTransform)_ring.transform, RingSize);
            _ring.type = Image.Type.Filled;
            _ring.fillMethod = Image.FillMethod.Radial360;
            _ring.fillOrigin = (int)Image.Origin360.Top;
            _ring.fillClockwise = true;
            _ring.fillAmount = 0f;

            _eyebrow = UiFactory.Label("Eyebrow", groupRect, "LOADING", UiType.Label, UiColor.TextSecondary, TextAnchor.UpperCenter);
            PlaceText(_eyebrow, RingLift - RingSize * 0.5f - (int)UiSpace.XL, UiFactory.LineHeight(UiType.Label));

            _destination = UiFactory.Label("Destination", groupRect, string.Empty, UiType.H2, UiColor.TextPrimary, TextAnchor.UpperCenter);
            PlaceText(_destination, RingLift - RingSize * 0.5f - (int)UiSpace.XL - UiFactory.LineHeight(UiType.Label) - (int)UiSpace.S, UiFactory.LineHeight(UiType.H2));

            _subline = UiFactory.Label("Subline", groupRect, string.Empty, UiType.Label, UiColor.TextSecondary, TextAnchor.UpperCenter);
            PlaceText(_subline, RingLift - RingSize * 0.5f - (int)UiSpace.XL - UiFactory.LineHeight(UiType.Label) - (int)UiSpace.S - UiFactory.LineHeight(UiType.H2), UiFactory.LineHeight(UiType.Label));

            _group.gameObject.SetActive(false);
            SetDoorsOpen(true);
            _canvas.enabled = false;
        }

        private RectTransform Door(string name, float anchorMinX, float anchorMaxX)
        {
            // Lacquer panel: SurfaceInset (Sumi900), never white (Reduce Flashes: nothing here flashes).
            Image door = UiFactory.NewImage(name, _root, UiColor.SurfaceInset, UiSprites.Solid);
            door.raycastTarget = true; // while closed, nothing underneath takes a click
            var rect = (RectTransform)door.transform;
            rect.anchorMin = new Vector2(anchorMinX, 0f);
            rect.anchorMax = new Vector2(anchorMaxX, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private void Place(RectTransform rect, float size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(0f, RingLift);
        }

        private void PlaceText(TMP_Text text, float top, float height)
        {
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(UiGrid.BindingPageWidth, height);
            rect.anchoredPosition = new Vector2(0f, top);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        // ================================================================== states

        private float HalfWidth
        {
            get { return Mathf.Max(960f, _root.rect.width * 0.5f); }
        }

        private void SetDoorsOpen(bool open)
        {
            float away = open ? HalfWidth + (int)UiSpace.S : 0f;
            _leftDoor.anchoredPosition = new Vector2(-away, 0f);
            _rightDoor.anchoredPosition = new Vector2(away, 0f);
        }

        /// <summary>Doors close (Move 280; RM or <paramref name="instant"/>: cut). Hides the pointer and clears focus while covered.</summary>
        public void Close(bool instant)
        {
            // A load that supersedes a reveal closes doors that are still shut: keep the cursor state from the first close.
            if (!s_covering)
            {
                _cursorWasVisible = Cursor.visible;
            }

            s_covering = true;
            _canvas.enabled = true;
            _group.gameObject.SetActive(false);
            Cursor.visible = false;
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            if (instant)
            {
                UiMotion.KillTarget(_leftDoor, false);
                UiMotion.KillTarget(_rightDoor, false);
                SetDoorsOpen(false);
                return;
            }

            UiMotion.Move(_leftDoor, Vector2.zero, UiDuration.Screen, UiEase.Move, UiMotionClass.Decor);
            UiMotion.Move(_rightDoor, Vector2.zero, UiDuration.Screen, UiEase.Move, UiMotionClass.Decor);
        }

        /// <summary>SCREEN depth: the loading group enters (PaneIn, Base 220) with the destination labels.</summary>
        public void ShowGroup(string destination, string subline)
        {
            _eyebrow.text = "LOADING";
            _destination.text = destination ?? string.Empty;
            _subline.text = subline ?? string.Empty;
            _ring.fillAmount = 0f;
            _shownFill = 0f;
            _ring.rectTransform.localScale = Vector3.one;
            _group.gameObject.SetActive(true);
            UiPresets.PaneIn(_group, null, 0f);
        }

        /// <summary>Honest progress: <paramref name="loadProgress"/> is AsyncOperation.progress (0..0.9). Written only on a visible change (Δ ≥ 1/256).</summary>
        public void SetProgress(float loadProgress)
        {
            float fill = Mathf.Clamp(loadProgress, 0f, LoadShare);
            if (fill < _shownFill || Mathf.Abs(fill - _shownFill) < 1f / 256f)
            {
                return; // never backwards, never a meaningless write
            }

            _shownFill = fill;
            _ring.fillAmount = fill;
        }

        /// <summary>Activation: ENTERING, the seal step (Stamp), then the last 36° close as the scene activates.</summary>
        public void Activate()
        {
            if (!GroupVisible)
            {
                return;
            }

            _eyebrow.text = "ENTERING";
            UiPresets.StampIn(_ring.rectTransform);
            UiMotion.Fill(_ring, 1f, UiDuration.Base, UiEase.Settle, UiMotionClass.Data);
        }

        /// <summary>Group out (Exit 140), then the doors part (Settle 280); <see cref="Revealed"/> fires half way. Returns the seconds it takes.</summary>
        public float Open(bool instant)
        {
            if (instant)
            {
                _group.gameObject.SetActive(false);
                SetDoorsOpen(true);
                FinishReveal();
                _canvas.enabled = false;
                return 0f;
            }

            float groupOut = 0f;
            if (GroupVisible)
            {
                UiPresets.PaneOut(_group);
                groupOut = UiMotionTokens.Seconds(UiDuration.Fast);
            }

            StartCoroutine(OpenDoors(groupOut));
            return groupOut + UiMotionTokens.Seconds(UiDuration.Screen);
        }

        private IEnumerator OpenDoors(float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSecondsRealtime(delay);
            }

            _group.gameObject.SetActive(false);
            float away = HalfWidth + (int)UiSpace.S;
            UiMotion.Move(_leftDoor, new Vector2(-away, 0f), UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor);
            UiMotion.Move(_rightDoor, new Vector2(away, 0f), UiDuration.Screen, UiEase.Settle, UiMotionClass.Decor);
            if (!UiMotion.ReducedMotion)
            {
                yield return new WaitForSecondsRealtime(UiMotionTokens.Seconds(UiDuration.Screen) * 0.5f);
            }

            FinishReveal();
            if (!UiMotion.ReducedMotion)
            {
                yield return new WaitForSecondsRealtime(UiMotionTokens.Seconds(UiDuration.Screen) * 0.5f);
            }

            _canvas.enabled = false;
        }

        private void FinishReveal()
        {
            s_covering = false;
            Cursor.visible = _cursorWasVisible;
            Action handlers = Revealed;
            if (handlers != null)
            {
                handlers();
            }
        }

        /// <summary>The destination label for a scene (UXW §2: real names only).</summary>
        public static string DestinationFor(string scene)
        {
            if (scene == PrototypeScenes.MainMenu) return "MAIN MENU";
            if (scene == PrototypeScenes.Selection) return "BEFORE THE RUN";
            if (scene == PrototypeScenes.Run) return "ROUND 1";
            if (scene == PrototypeScenes.Results) return "RUN RESULTS";
            return string.Empty;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_instance = null;
            s_covering = false;
            Revealed = null;
        }
    }
}
