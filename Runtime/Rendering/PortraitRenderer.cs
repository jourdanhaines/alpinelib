using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Still portraits of a subject (a character copy, a vehicle copy) into small render targets: the
    /// subject is moved to the stage position, framed by a <see cref="PortraitFraming"/>, drawn by a
    /// hidden camera limited to a culling mask, and left at the stage. Rendering is a no-op returning
    /// false on a null graphics device.
    /// </summary>
    public sealed class PortraitRenderer : IDisposable {
        private readonly PortraitRendererSettings _settings;
        private readonly List<KeyValuePair<GameObject, int>> _forcedLayers = new List<KeyValuePair<GameObject, int>>();
        private Camera _camera;

        /// <summary>Creates the hidden portrait camera.</summary>
        public PortraitRenderer(PortraitRendererSettings settings) {
            _settings = settings ?? new PortraitRendererSettings();
            _camera = CaptureCameraSetup.Create("PortraitRendererCamera");
        }

        /// <summary>The settings this renderer draws with.</summary>
        public PortraitRendererSettings Settings => _settings;

        /// <summary>A square sRGB colour target of <see cref="PortraitRendererSettings.Size"/>, with depth; the caller owns it.</summary>
        public RenderTexture CreateTarget() {
            var descriptor = new RenderTextureDescriptor(_settings.Size, _settings.Size, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.D24_UNorm_S8_UInt) {
                msaaSamples = Mathf.Max(1, _settings.MsaaSamples),
                useMipMap = false,
            };
            var target = new RenderTexture(descriptor) { name = "Portrait", filterMode = FilterMode.Bilinear };
            return target;
        }

        /// <summary>Moves <paramref name="subject"/> to the stage position, keeping its rotation.</summary>
        public void Stage(Transform subject) {
            subject.position = _settings.StagePosition;
        }

        /// <summary>
        /// Stages and draws <paramref name="subject"/> into <paramref name="target"/>, seeing only
        /// <paramref name="cullingMask"/>. With <paramref name="forceLayer"/> every object under the subject
        /// is moved onto the mask's lowest layer for the render and put back after.
        /// </summary>
        public bool Render(Transform subject, PortraitFraming framing, RenderTexture target, int cullingMask, bool forceLayer = false) {
            if (!CaptureRender.CanRender || subject == null || target == null || _camera == null) return false;

            Stage(subject);
            Pose pose = PortraitFraming.ComputeCameraPose(framing, new Pose(subject.position, subject.rotation));
            float distance = PortraitFraming.ComputeDistance(framing);
            CaptureCameraSetup.Configure(_camera, cullingMask, _settings.Background);
            _camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            _camera.orthographic = false;
            _camera.fieldOfView = framing.FieldOfView;
            _camera.aspect = 1f;
            _camera.nearClipPlane = Mathf.Max(0.01f, distance - framing.FocusRadius * 4f);
            _camera.farClipPlane = distance + framing.FocusRadius * 4f;
            PortraitFraming.ApplyLensShift(_camera, framing);

            if (forceLayer) ForceLayer(subject, LowestLayer(cullingMask));
            try {
                using (new CaptureEnvironmentScope(_settings.Lighting, _camera)) {
                    return CaptureRender.Submit(_camera, target);
                }
            } finally {
                RestoreLayers();
            }
        }

        /// <summary>Destroys the hidden camera.</summary>
        public void Dispose() {
            if (_camera != null) CaptureRender.Destroy(_camera.gameObject);
            _camera = null;
        }

        /// <summary>Lowest layer index set in <paramref name="mask"/>, 0 for an empty mask.</summary>
        public static int LowestLayer(int mask) {
            for (int layer = 0; layer < 32; layer++) {
                if ((mask & (1 << layer)) != 0) return layer;
            }

            return 0;
        }

        private void ForceLayer(Transform subject, int layer) {
            foreach (Transform child in subject.GetComponentsInChildren<Transform>(true)) {
                _forcedLayers.Add(new KeyValuePair<GameObject, int>(child.gameObject, child.gameObject.layer));
                child.gameObject.layer = layer;
            }
        }

        private void RestoreLayers() {
            foreach (KeyValuePair<GameObject, int> entry in _forcedLayers) {
                if (entry.Key != null) entry.Key.layer = entry.Value;
            }

            _forcedLayers.Clear();
        }
    }
}
