using UnityEngine;
using UnityEngine.UI;

namespace AlpineLib.Maps {
    /// <summary>
    /// One map marker: a circular portrait under a frame ring, plus an arrow that orbits the frame and
    /// points at the target while the marker is pinned to the viewport edge. Nothing on it takes raycasts.
    /// </summary>
    public class MapMarker : MonoBehaviour {
        [SerializeField] private RawImage portrait;
        [SerializeField] private Image frame;
        [SerializeField] private Image arrow;

        private MapMarkerStyle _style;

        /// <summary>Portrait image (cut to a circle by its material).</summary>
        public RawImage Portrait => portrait;

        /// <summary>Frame ring.</summary>
        public Image Frame => frame;

        /// <summary>Off-view arrow.</summary>
        public Image Arrow => arrow;

        /// <summary>Style last applied.</summary>
        public MapMarkerStyle Style => _style;

        /// <summary>Map position the marker tracks.</summary>
        public MapPoint WorldPosition { get; set; }

        /// <summary>True while hidden by its layer.</summary>
        public bool IsHidden { get; set; }

        /// <summary>Placement at the last layout.</summary>
        public MapMarkerPlacement Placement { get; private set; }

        /// <summary>
        /// Builds a marker under <paramref name="parent"/>: root (centre-anchored) with Portrait (RawImage on
        /// <paramref name="cutoutMaterial"/>), Frame and Arrow children, styled by <paramref name="style"/>.
        /// </summary>
        public static MapMarker Build(Transform parent, MapMarkerStyle style, Material cutoutMaterial) {
            var root = new GameObject("MapMarker", typeof(RectTransform));
            root.layer = parent != null ? parent.gameObject.layer : 0;
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            CentreAnchor(rootRect);

            MapMarker marker = root.AddComponent<MapMarker>();
            marker.portrait = CreateChild<RawImage>(rootRect, "Portrait");
            marker.portrait.material = cutoutMaterial;
            marker.frame = CreateChild<Image>(rootRect, "Frame");
            marker.arrow = CreateChild<Image>(rootRect, "Arrow");
            marker.arrow.gameObject.SetActive(false);
            marker.Apply(style);
            return marker;
        }

        /// <summary>Applies sizes and sprites from <paramref name="style"/>.</summary>
        public void Apply(MapMarkerStyle style) {
            _style = style ?? new MapMarkerStyle();
            var rect = (RectTransform)transform;
            rect.sizeDelta = new Vector2(_style.Size, _style.Size);
            if (portrait != null) portrait.rectTransform.sizeDelta = rect.sizeDelta;
            if (frame != null) {
                frame.sprite = _style.FrameSprite;
                frame.rectTransform.sizeDelta = rect.sizeDelta;
            }

            if (arrow == null) return;

            arrow.sprite = _style.ArrowSprite;
            arrow.rectTransform.sizeDelta = new Vector2(_style.ArrowSize, _style.ArrowHeight());
        }

        /// <summary>Shows <paramref name="texture"/> in the portrait circle.</summary>
        public void SetPortrait(Texture texture) {
            if (portrait == null) return;

            portrait.texture = texture;
            portrait.enabled = texture != null;
        }

        /// <summary>Moves to <paramref name="placement"/>; the arrow shows, orbiting at <paramref name="arrowOrbit"/>, only while clamped.</summary>
        public void SetPlacement(MapMarkerPlacement placement, float arrowOrbit) {
            Placement = placement;
            ((RectTransform)transform).anchoredPosition = placement.Position;
            if (arrow == null) return;

            arrow.gameObject.SetActive(placement.IsClamped);
            if (!placement.IsClamped) return;

            arrow.rectTransform.anchoredPosition = MapMarkerMath.OrbitOffset(placement.Direction, arrowOrbit);
            arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, MapMarkerMath.AngleDegrees(placement.Direction));
        }

        private static T CreateChild<T>(RectTransform parent, string name) where T : Graphic {
            var child = new GameObject(name, typeof(RectTransform));
            child.layer = parent.gameObject.layer;
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            CentreAnchor(rect);
            T graphic = child.AddComponent<T>();
            graphic.raycastTarget = false;
            return graphic;
        }

        private static void CentreAnchor(RectTransform rect) {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
        }
    }
}
