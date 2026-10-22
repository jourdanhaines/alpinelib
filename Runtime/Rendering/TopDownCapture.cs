using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>
    /// Orthographic straight-down capture of a <see cref="TopDownCaptureArea"/>: image up is world +Z
    /// (north), image right is world +X (east).
    /// </summary>
    public static class TopDownCapture {
        /// <summary>The camera rotation: looking down, up vector north.</summary>
        public static readonly Quaternion DownRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

        /// <summary>Camera position and rotation for <paramref name="area"/>.</summary>
        public static Pose ComputePose(TopDownCaptureArea area) {
            return new Pose(area.Centre + Vector3.up * area.Height, DownRotation);
        }

        /// <summary>Poses <paramref name="camera"/> orthographically over <paramref name="area"/>.</summary>
        public static void PoseCamera(Camera camera, TopDownCaptureArea area) {
            Pose pose = ComputePose(area);
            camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
            camera.orthographic = true;
            camera.orthographicSize = area.Size.y * 0.5f;
            camera.aspect = area.Size.y > 0f ? area.Size.x / area.Size.y : 1f;
            camera.nearClipPlane = area.NearClip;
            camera.farClipPlane = area.FarClip;
        }

        /// <summary>
        /// Poses <paramref name="camera"/> and renders <paramref name="area"/> into <paramref name="target"/>
        /// under <paramref name="lighting"/>; false on a null device.
        /// </summary>
        public static bool Render(Camera camera, TopDownCaptureArea area, RenderTexture target, CaptureLighting lighting) {
            if (!CaptureRender.CanRender) return false;

            PoseCamera(camera, area);
            using (new CaptureEnvironmentScope(lighting, camera)) {
                return CaptureRender.Submit(camera, target);
            }
        }
    }
}
