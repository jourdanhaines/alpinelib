using UnityEngine;

namespace AlpineLib.Cameras {
    /// <summary>
    /// Places a flat surface squarely in front of a camera: how far away it must sit to fit the view,
    /// and the world pose that faces it back at the eye.
    /// </summary>
    /// <remarks>
    /// The surface convention is a quad in its local XY plane whose readable face points down local −Z,
    /// so <see cref="InFrontOf"/> simply copies the eye's rotation: local +Z then runs away from the
    /// camera and the front faces it.
    /// </remarks>
    public static class CameraFacingPose {
        /// <summary>
        /// Distance at which a surface of <paramref name="surfaceSize"/> (width, height in metres) fills at
        /// most <paramref name="maxViewFraction"/> of the camera's view on both axes, never closer than
        /// <paramref name="preferredDistance"/>. Perspective cameras only; a null camera returns the preference.
        /// </summary>
        public static float FitDistance(Camera camera, Vector2 surfaceSize, float preferredDistance, float maxViewFraction) {
            if (camera == null) return preferredDistance;

            float viewFraction = Mathf.Max(maxViewFraction, 0.01f);
            float tanHalfFov = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            float aspect = Mathf.Max(camera.aspect, 0.01f);

            float heightFit = surfaceSize.y / (2f * tanHalfFov * viewFraction);
            float widthFit = surfaceSize.x / (2f * tanHalfFov * aspect * viewFraction);

            return Mathf.Max(preferredDistance, heightFit, widthFit);
        }

        /// <summary>
        /// The pose <paramref name="distance"/> metres along the eye's forward, rotated so a surface's −Z
        /// front faces the eye and its +Y matches the eye's up.
        /// </summary>
        public static Pose InFrontOf(Transform eye, float distance) {
            Vector3 position = eye.position + eye.forward * distance;
            Quaternion rotation = Quaternion.LookRotation(eye.forward, eye.up);
            return new Pose(position, rotation);
        }
    }
}
