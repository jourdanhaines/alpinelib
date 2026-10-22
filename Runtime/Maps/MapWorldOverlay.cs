using UnityEngine;
using UnityEngine.UI;

namespace AlpineLib.Maps {
    /// <summary>
    /// A texture laid over the map at a world rectangle (e.g. a live top-down render of a vehicle): centre,
    /// size in metres and heading (bearing of the texture's up), re-placed for the view each <c>LateUpdate</c>.
    /// Hidden while it has no texture.
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class MapWorldOverlay : MonoBehaviour {
        [Tooltip("Viewport whose view places the overlay.")]
        [SerializeField] private MapViewportGraphic viewport;

        private RawImage _image;
        private MapPoint _centre;
        private Vector2 _sizeMetres;
        private float _headingDegrees;

        /// <summary>Viewport placing the overlay.</summary>
        public MapViewportGraphic Viewport {
            get => viewport;
            set => viewport = value;
        }

        /// <summary>Overlay image.</summary>
        public RawImage Image => _image != null ? _image : _image = GetComponent<RawImage>();

        /// <summary>Covers the world rect at <paramref name="centre"/>, <paramref name="sizeMetres"/> (X east, Y north before heading) turned to <paramref name="headingDegrees"/>.</summary>
        public void SetWorldRect(MapPoint centre, Vector2 sizeMetres, float headingDegrees) {
            _centre = centre;
            _sizeMetres = sizeMetres;
            _headingDegrees = headingDegrees;
        }

        /// <summary>Shows <paramref name="texture"/>; null hides the overlay.</summary>
        public void SetTexture(Texture texture) {
            Image.texture = texture;
            Image.enabled = texture != null;
        }

        /// <summary>Re-places the overlay for the current view.</summary>
        public void Layout() {
            MapView view = viewport != null ? viewport.View : null;
            if (view == null || Image.texture == null) return;

            RectTransform rect = Image.rectTransform;
            float unitsPerMetre = (float)(1.0 / view.MetresPerUnit);
            rect.anchoredPosition = view.WorldToViewport(_centre);
            rect.sizeDelta = _sizeMetres * unitsPerMetre;
            rect.localEulerAngles = new Vector3(0f, 0f, (float)(view.RotationDegrees - _headingDegrees));
        }

        protected virtual void LateUpdate() {
            Layout();
        }
    }
}
