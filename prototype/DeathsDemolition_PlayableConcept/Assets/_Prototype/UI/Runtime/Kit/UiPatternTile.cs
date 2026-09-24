using UnityEngine;
using UnityEngine.UI;

namespace DD.Prototype.UI
{
    /// <summary>
    /// WP-068 A6 · a one-channel pattern tile (kumiko, washi) laid over a surface at a fixed on-screen scale. The tile is
    /// a single BC4 channel, so it draws through DD/UI/MaskTint (red = coverage of the graphic's colour); the uvRect
    /// follows the rect so the pattern never stretches - recomputed only when the rect changes, never per frame.
    /// Absent tile or shader: the graphic is not drawn (a plain surface, never a red rectangle).
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    [DisallowMultipleComponent]
    public sealed class UiPatternTile : MonoBehaviour
    {
        private RawImage _image;
        private float _tilePixels = 256f;

        /// <summary>A stretched pattern layer under <paramref name="parent"/>, tinted <paramref name="color"/> at <paramref name="alpha"/>.</summary>
        public static UiPatternTile Create(string name, RectTransform parent, string tileResource, float tilePixels, UiColor color, float alpha)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            UiFactory.Fill(rect);
            var image = go.GetComponent<RawImage>();
            image.raycastTarget = false;
            Color tint = UiTokens.Get(color);
            tint.a = alpha;
            image.color = tint;

            UiPatternTile tile = go.AddComponent<UiPatternTile>();
            tile._image = image;
            tile._tilePixels = Mathf.Max(1f, tilePixels);
            Texture texture = Resources.Load<Texture>(tileResource);
            Material mask = UiBackdrop.MaskMaterial();
            if (texture == null || mask == null)
            {
                go.SetActive(false);
                return tile;
            }

            image.texture = texture;
            image.material = mask;
            tile.Fit();
            return tile;
        }

        /// <summary>The on-screen size of one tile in canvas px.</summary>
        public float TilePixels
        {
            get { return _tilePixels; }
        }

        private void OnRectTransformDimensionsChange()
        {
            Fit();
        }

        private void Fit()
        {
            if (_image == null)
            {
                return;
            }

            Rect r = ((RectTransform)transform).rect;
            var uv = new Rect(0f, 0f, Mathf.Max(0.01f, r.width / _tilePixels), Mathf.Max(0.01f, r.height / _tilePixels));
            if (_image.uvRect != uv)
            {
                _image.uvRect = uv;
            }
        }
    }
}
