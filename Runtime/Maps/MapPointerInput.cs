using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AlpineLib.Maps {
    /// <summary>
    /// Pointer gestures on a map viewport: drag pans (the point under the pointer follows it), the wheel
    /// zooms about the pointer (one notch = <see cref="ZoomStep"/>), and a double click raises
    /// <see cref="DoubleClicked"/>. An optional <see cref="IMapInputLock"/> is held for the length of a drag.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class MapPointerInput : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, IPointerClickHandler {
        [Tooltip("Viewport whose view the gestures move.")]
        [SerializeField] private MapViewportGraphic viewport;

        [Tooltip("Zoom factor per wheel notch.")]
        [SerializeField] private float zoomStep = 1.25f;

        [Tooltip("Scroll delta one wheel notch reports.")]
        [SerializeField] private float scrollPerNotch = 6f;

        private Vector2 _lastPoint;
        private bool _dragging;
        private bool _lastClickDragged;

        /// <summary>Raised on a double click, with the viewport point clicked.</summary>
        public event Action<Vector2> DoubleClicked;

        /// <summary>Held while a drag is in progress; may be null.</summary>
        public IMapInputLock InputLock { get; set; }

        /// <summary>Zoom factor per wheel notch.</summary>
        public float ZoomStep => zoomStep;

        /// <summary>True while a drag is in progress.</summary>
        public bool IsDragging => _dragging;

        /// <summary>Viewport the gestures move.</summary>
        public MapViewportGraphic Viewport {
            get => viewport;
            set => viewport = value;
        }

        /// <summary>Zoom factor for a scroll of <paramref name="scrollY"/> (positive scrolls up and zooms in).</summary>
        public static double ZoomFactor(float scrollY, float zoomStep, float scrollPerNotch) {
            return Math.Pow(zoomStep, -scrollY / Math.Max(1e-3f, scrollPerNotch));
        }

        public void OnPointerDown(PointerEventData eventData) {
            // Claims the press so drags and clicks route here.
        }

        public void OnBeginDrag(PointerEventData eventData) {
            if (eventData.button != PointerEventData.InputButton.Left || !TryGetPoint(eventData, out _lastPoint)) return;

            _dragging = true;
            InputLock?.BeginMapGesture();
        }

        public void OnDrag(PointerEventData eventData) {
            if (!_dragging || !TryGetPoint(eventData, out Vector2 point)) return;

            View()?.Pan(point - _lastPoint);
            _lastPoint = point;
        }

        public void OnEndDrag(PointerEventData eventData) {
            EndDrag();
        }

        public void OnScroll(PointerEventData eventData) {
            if (eventData.scrollDelta.y == 0f || !TryGetPoint(eventData, out Vector2 point, false)) return;

            View()?.ZoomAbout(point, ZoomFactor(eventData.scrollDelta.y, zoomStep, scrollPerNotch));
        }

        // A drag's release still counts as a click, so a quick click after a drag would read as a double click.
        public void OnPointerClick(PointerEventData eventData) {
            bool afterDrag = _lastClickDragged;
            _lastClickDragged = eventData.dragging;
            if (eventData.dragging || afterDrag || eventData.clickCount != 2) return;
            if (!TryGetPoint(eventData, out Vector2 point)) return;

            DoubleClicked?.Invoke(point);
        }

        protected virtual void OnDisable() {
            EndDrag();
        }

        private void EndDrag() {
            if (!_dragging) return;

            _dragging = false;
            InputLock?.EndMapGesture();
        }

        private bool TryGetPoint(PointerEventData eventData, out Vector2 point, bool fromPress = true) {
            RectTransform rect = viewport != null ? viewport.rectTransform : (RectTransform)transform;
            return MapPointerResolver.TryGetViewportPoint(eventData, rect, out point, fromPress);
        }

        private MapView View() {
            return viewport != null ? viewport.View : null;
        }
    }
}
