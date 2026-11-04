using AlpineLib.Rendering;
using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>
    /// Which cameras draw sky decoration: game, scene-view and preview cameras whose culling mask holds the sky's
    /// layer, never a capture camera (map tiles, portraits), whose masks may hold that layer for other reasons.
    /// </summary>
    public static class SkyCameraFilter {
        private const CameraType DrawingTypes = CameraType.Game | CameraType.SceneView | CameraType.Preview;

        /// <summary>True when <paramref name="camera"/> should draw sky geometry on <paramref name="layer"/>.</summary>
        public static bool ShouldDraw(Camera camera, int layer) {
            if (camera == null || layer < 0 || layer > 31) return false;
            if ((camera.cullingMask & (1 << layer)) == 0) return false;
            if ((camera.cameraType & DrawingTypes) == 0) return false;

            return !CaptureCameraSetup.IsCaptureCamera(camera);
        }
    }
}
