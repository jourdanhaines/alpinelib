using AlpineLib.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AlpineLib.Maps {
    /// <summary>
    /// Turns a pointer event into a viewport point (canvas units from the rect's centre). A canvas drawn
    /// onto a mesh reports positions in the pointer camera's screen space, so when the event came through an
    /// <see cref="ICanvasPointRaycaster"/> the position is re-projected into canvas pixels first; otherwise
    /// the screen position is used as is.
    /// </summary>
    public static class MapPointerResolver {
        /// <summary>
        /// Viewport point under <paramref name="eventData"/>'s position; false when it misses the canvas.
        /// <paramref name="fromPress"/> resolves through the raycaster that took the press (drags, clicks);
        /// otherwise through the one under the pointer now (the wheel, which has no press of its own).
        /// </summary>
        public static bool TryGetViewportPoint(PointerEventData eventData, RectTransform rect, out Vector2 viewportPoint, bool fromPress = true) {
            viewportPoint = default;
            if (eventData == null || rect == null) return false;

            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera canvasCamera = CanvasCamera(canvas);
            BaseRaycaster module = ModuleOf(eventData, fromPress);
            if (module is ICanvasPointRaycaster raycaster) {
                if (!raycaster.TryGetCanvasPoint(eventData.position, out Vector2 canvasPixel)) return false;

                return TryCanvasPixelToViewport(rect, canvasPixel, canvasCamera, out viewportPoint);
            }

            return TryCanvasPixelToViewport(rect, eventData.position, canvasCamera, out viewportPoint);
        }

        /// <summary>
        /// Viewport point of <paramref name="canvasPixel"/> (a pixel of <paramref name="canvasCamera"/>'s
        /// target, or of the screen for an overlay canvas with a null camera).
        /// </summary>
        public static bool TryCanvasPixelToViewport(RectTransform rect, Vector2 canvasPixel, Camera canvasCamera, out Vector2 viewportPoint) {
            viewportPoint = default;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, canvasPixel, canvasCamera, out Vector2 local)) return false;

            viewportPoint = local - rect.rect.center;
            return true;
        }

        // The press raycast outlives the press, so a wheel event would otherwise inherit a stale module.
        private static BaseRaycaster ModuleOf(PointerEventData eventData, bool fromPress) {
            BaseRaycaster first = fromPress ? eventData.pointerPressRaycast.module : eventData.pointerCurrentRaycast.module;
            BaseRaycaster second = fromPress ? eventData.pointerCurrentRaycast.module : eventData.pointerPressRaycast.module;
            return first != null ? first : second;
        }

        private static Camera CanvasCamera(Canvas canvas) {
            if (canvas == null) return null;

            Canvas root = canvas.rootCanvas;
            return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
        }
    }
}
