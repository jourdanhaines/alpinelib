using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Puts a camera into capture shape: disabled (rendered on demand only), solid clear, no post,
    /// no shadows, no post-process anti-aliasing and no depth/opaque textures. MSAA is left to the target.
    /// </summary>
    /// <remarks>
    /// Every camera made by <see cref="Create"/>, shaped by <see cref="Configure"/> or rendered through
    /// <see cref="CaptureRender.Submit"/> is remembered, so world decoration that must stay out of captures (sky,
    /// effects) can ask <see cref="IsCaptureCamera"/>; <see cref="Unregister"/> returns a camera to normal duty.
    /// </remarks>
    public static class CaptureCameraSetup {
        private static readonly Dictionary<int, Camera> CaptureCameras = new Dictionary<int, Camera>();
        private static readonly List<int> DestroyedIds = new List<int>();

        /// <summary>A hidden, disabled capture camera on a new GameObject that is never saved.</summary>
        public static Camera Create(string name) {
            var host = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            var camera = host.AddComponent<Camera>();
            camera.enabled = false;
            Register(camera);
            return camera;
        }

        /// <summary>True when <paramref name="camera"/> was made by <see cref="Create"/> or shaped by <see cref="Configure"/>.</summary>
        public static bool IsCaptureCamera(Camera camera) {
            return camera != null && CaptureCameras.ContainsKey(camera.GetInstanceID());
        }

        /// <summary>Marks <paramref name="camera"/> as a capture camera without changing it; a new one first forgets destroyed cameras.</summary>
        public static void Register(Camera camera) {
            if (camera == null) return;

            int id = camera.GetInstanceID();
            if (CaptureCameras.ContainsKey(id)) return;

            ForgetDestroyed();
            CaptureCameras.Add(id, camera);
        }

        /// <summary>Capture cameras remembered (destroyed ones linger until the next new registration).</summary>
        public static int RegisteredCount => CaptureCameras.Count;

        /// <summary>Forgets <paramref name="camera"/> as a capture camera (a borrowed camera handed back to gameplay).</summary>
        public static void Unregister(Camera camera) {
            if (camera != null) CaptureCameras.Remove(camera.GetInstanceID());
        }

        private static void ForgetDestroyed() {
            DestroyedIds.Clear();
            foreach (KeyValuePair<int, Camera> entry in CaptureCameras) {
                if (entry.Value == null) DestroyedIds.Add(entry.Key);
            }
            foreach (int id in DestroyedIds) CaptureCameras.Remove(id);
        }

        /// <summary>Configures <paramref name="camera"/> to draw <paramref name="cullingMask"/> over a <paramref name="clear"/> background.</summary>
        public static void Configure(Camera camera, int cullingMask, Color clear) {
            Register(camera);
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = clear;
            camera.cullingMask = cullingMask;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.useOcclusionCulling = false;

            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (data == null) return;

            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
            data.stopNaN = false;
            data.dithering = false;
        }
    }
}
