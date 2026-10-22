using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineLib.Rendering {
    /// <summary>
    /// One off-screen render of a disabled camera into a texture: a pipeline render request where the
    /// pipeline supports it, else <see cref="Camera.Render"/>. Every call is a no-op returning false on a
    /// null graphics device, so <c>-nographics</c> gates can drive capture code.
    /// </summary>
    public static class CaptureRender {
        /// <summary>False on a null graphics device (batchmode <c>-nographics</c>), where nothing renders.</summary>
        public static bool CanRender => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>Renders <paramref name="camera"/> into <paramref name="target"/>; false when nothing was rendered.</summary>
        public static bool Submit(Camera camera, RenderTexture target) {
            if (!CanRender || camera == null || target == null) return false;

            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request)) {
                RenderPipeline.SubmitRenderRequest(camera, request);
                return true;
            }

            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = target;
            camera.Render();
            camera.targetTexture = previous;
            return true;
        }

        /// <summary>Destroys a capture resource (texture, camera host) in play and edit mode alike.</summary>
        public static void Destroy(Object target) {
            if (target == null) return;

            if (Application.isPlaying) {
                Object.Destroy(target);
                return;
            }

            Object.DestroyImmediate(target);
        }
    }
}
