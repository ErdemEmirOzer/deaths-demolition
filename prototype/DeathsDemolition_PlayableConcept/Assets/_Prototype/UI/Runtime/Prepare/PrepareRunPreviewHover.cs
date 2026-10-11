using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-106 · Prepare Run's preview hint: the pointer entering / leaving the 3D view (PrepareRunStage's RawImage, which owns
    /// the drag, the wheel and the double click). The screen shows the hint's words only while the pointer is over the view - at
    /// rest the corner glyph alone says it (R2 U1: fewer letters). It sits on the view's own GameObject, beside the stage, so
    /// the stage keeps every pointer event it handles; enter and exit reach both. No Update, nothing allocated per event.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrepareRunPreviewHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private Action<bool> _changed;

        /// <summary>True while the pointer is over the view.</summary>
        public bool Hovered { get; private set; }

        /// <summary><paramref name="changed"/> hears the pointer enter (true) and leave (false).</summary>
        public void Bind(Action<bool> changed)
        {
            _changed = changed;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            Set(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            Set(false);
        }

        private void OnDisable()
        {
            Set(false);
        }

        private void Set(bool over)
        {
            if (over == Hovered)
            {
                return;
            }

            Hovered = over;
            if (_changed != null)
            {
                _changed(over);
            }
        }
    }
}
