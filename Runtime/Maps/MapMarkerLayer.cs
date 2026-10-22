using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Maps {
    /// <summary>
    /// Lays out markers over a viewport each <c>LateUpdate</c>: world position → viewport point → edge
    /// clamp (inset = half the marker plus <see cref="EdgeGap"/>), arrow orbit = half the marker plus
    /// <see cref="ArrowGap"/>. Higher-priority markers draw on top. Sits on a rect matching the viewport.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MapMarkerLayer : MonoBehaviour {
        [Tooltip("Viewport whose view places the markers.")]
        [SerializeField] private MapViewportGraphic viewport;

        [Tooltip("Material on AlpineLib/UI/CircleCutout for marker portraits.")]
        [SerializeField] private Material cutoutMaterial;

        [Tooltip("Gap between a clamped marker's edge and the viewport edge, canvas units.")]
        [SerializeField] private float edgeGap = 8f;

        [Tooltip("Gap between the marker's edge and the arrow's centre, canvas units.")]
        [SerializeField] private float arrowGap = 6f;

        private readonly List<MapMarker> _markers = new List<MapMarker>();

        /// <summary>Gap between a clamped marker and the viewport edge.</summary>
        public float EdgeGap => edgeGap;

        /// <summary>Gap between the marker edge and the arrow.</summary>
        public float ArrowGap => arrowGap;

        /// <summary>Markers, back to front.</summary>
        public IReadOnlyList<MapMarker> Markers => _markers;

        /// <summary>Viewport placing the markers.</summary>
        public MapViewportGraphic Viewport {
            get => viewport;
            set => viewport = value;
        }

        /// <summary>Adds a marker in <paramref name="style"/>, ordered by its priority.</summary>
        public MapMarker Add(MapMarkerStyle style) {
            MapMarker marker = MapMarker.Build(transform, style, cutoutMaterial);
            _markers.Add(marker);
            SortByPriority();
            return marker;
        }

        /// <summary>Removes and destroys <paramref name="marker"/>.</summary>
        public void Remove(MapMarker marker) {
            if (marker == null || !_markers.Remove(marker)) return;

            AlpineLib.Rendering.CaptureRender.Destroy(marker.gameObject);
        }

        /// <summary>Points <paramref name="marker"/> at <paramref name="position"/>.</summary>
        public void SetWorldPosition(MapMarker marker, MapPoint position) {
            marker.WorldPosition = position;
        }

        /// <summary>Hides or shows <paramref name="marker"/>.</summary>
        public void SetHidden(MapMarker marker, bool hidden) {
            marker.IsHidden = hidden;
            marker.gameObject.SetActive(!hidden);
        }

        /// <summary>Places every visible marker for the current view.</summary>
        public void Layout() {
            MapView view = viewport != null ? viewport.View : null;
            if (view == null) return;

            foreach (MapMarker marker in _markers) {
                if (marker.IsHidden) continue;

                Place(marker, view);
            }
        }

        protected virtual void LateUpdate() {
            Layout();
        }

        private void Place(MapMarker marker, MapView view) {
            float half = marker.Style.Size * 0.5f;
            Vector2 point = view.WorldToViewport(marker.WorldPosition);
            MapMarkerPlacement placement = MapMarkerMath.Place(point, view.ViewportSize, half + edgeGap, viewport.IsCircle);
            marker.SetPlacement(placement, half + arrowGap);
        }

        private void SortByPriority() {
            // Stable: equal priorities keep insertion order.
            var ordered = new List<MapMarker>(_markers);
            _markers.Clear();
            foreach (MapMarker marker in ordered) InsertByPriority(marker);
            for (int index = 0; index < _markers.Count; index++) _markers[index].transform.SetSiblingIndex(index);
        }

        private void InsertByPriority(MapMarker marker) {
            int index = _markers.Count;
            while (index > 0 && _markers[index - 1].Style.Priority > marker.Style.Priority) index--;
            _markers.Insert(index, marker);
        }
    }
}
