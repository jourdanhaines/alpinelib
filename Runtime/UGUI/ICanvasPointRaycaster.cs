using UnityEngine;

namespace AlpineLib.UI {
    /// <summary>
    /// A raycaster whose canvas is not drawn at the pointer's screen position (a canvas rendered onto a
    /// mesh), so drag handlers can re-project <see cref="UnityEngine.EventSystems.PointerEventData.position"/>
    /// into canvas pixels themselves.
    /// </summary>
    public interface ICanvasPointRaycaster {
        /// <summary>
        /// Canvas-camera pixel under <paramref name="screenPoint"/> (a pointer-camera screen position);
        /// false when the pointer misses the canvas surface.
        /// </summary>
        bool TryGetCanvasPoint(Vector2 screenPoint, out Vector2 canvasPixel);
    }
}
