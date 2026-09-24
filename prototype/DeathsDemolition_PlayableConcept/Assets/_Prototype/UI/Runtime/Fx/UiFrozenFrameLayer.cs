using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>The layer: enabled the frame after the capture (the pass writes during that frame's rendering).</summary>
    internal sealed class UiFrozenFrameLayer : MonoBehaviour
    {
        private Canvas _canvas;
        private RawImage _image;

        public bool Showing => _canvas != null && _canvas.enabled;

        public void Bind(Canvas canvas, RawImage image)
        {
            _canvas = canvas;
            _image = image;
        }

        public void Show(bool on)
        {
            if (_canvas == null)
            {
                return;
            }

            if (on)
            {
                _image.texture = UiFrozenFrame.Target;
            }

            if (_canvas.enabled != on)
            {
                _canvas.enabled = on;
            }
        }

        private void LateUpdate()
        {
            bool want = UiFrozenFrame.CaptureReady && Time.frameCount > UiFrozenFrame.CapturedFrame;
            if (want != Showing)
            {
                Show(want);
            }
        }
    }
}
