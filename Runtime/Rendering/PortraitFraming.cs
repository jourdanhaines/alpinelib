using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>
    /// How a portrait camera frames a subject, in the subject's own space: a focus sphere, the view
    /// angle (yaw 180 looks at the subject's front), a vertical field of view, and how much of the
    /// frame height the focus sphere fills; an optional lens shift moves the image without turning the camera.
    /// </summary>
    public readonly struct PortraitFraming {
        /// <summary>Builds a framing.</summary>
        public PortraitFraming(Vector3 focusCentre, float focusRadius, float pitchDegrees, float yawDegrees, float fieldOfView, float fill)
            : this(focusCentre, focusRadius, pitchDegrees, yawDegrees, fieldOfView, fill, Vector2.zero) {
        }

        /// <summary>Builds a framing whose image is shifted by <paramref name="lensShift"/> (see <see cref="LensShift"/>).</summary>
        public PortraitFraming(Vector3 focusCentre, float focusRadius, float pitchDegrees, float yawDegrees, float fieldOfView, float fill, Vector2 lensShift) {
            LensShift = lensShift;
            FocusCentre = focusCentre;
            FocusRadius = focusRadius;
            PitchDegrees = pitchDegrees;
            YawDegrees = yawDegrees;
            FieldOfView = fieldOfView;
            Fill = fill;
        }

        /// <summary>Subject-local centre of the focus sphere.</summary>
        public Vector3 FocusCentre { get; }

        /// <summary>Focus sphere radius, metres.</summary>
        public float FocusRadius { get; }

        /// <summary>Camera pitch, positive looking down.</summary>
        public float PitchDegrees { get; }

        /// <summary>Camera yaw relative to the subject's forward; 180 faces the subject's front.</summary>
        public float YawDegrees { get; }

        /// <summary>Vertical field of view, degrees.</summary>
        public float FieldOfView { get; }

        /// <summary>Fraction of the frame height the focus sphere's diameter spans.</summary>
        public float Fill { get; }

        /// <summary>
        /// Off-axis shift in half-frames: the point that would project at this position (x right, y up, ±1 at
        /// the edges) lands at the frame centre instead. Keeps a tilted view's perspective while recentring it.
        /// </summary>
        public Vector2 LensShift { get; }

        /// <summary>Applies <paramref name="framing"/>'s lens shift to <paramref name="camera"/>'s projection, or resets it.</summary>
        public static void ApplyLensShift(Camera camera, PortraitFraming framing) {
            camera.ResetProjectionMatrix();
            if (framing.LensShift == Vector2.zero) return;

            Matrix4x4 projection = camera.projectionMatrix;
            projection[0, 2] = framing.LensShift.x;
            projection[1, 2] = framing.LensShift.y;
            camera.projectionMatrix = projection;
        }

        /// <summary>A framing around <paramref name="localBounds"/> (subject-local), sphere = the bounds' bounding sphere.</summary>
        public static PortraitFraming FromBounds(Bounds localBounds, float pitchDegrees, float yawDegrees, float fieldOfView, float fill) {
            return new PortraitFraming(localBounds.center, localBounds.extents.magnitude, pitchDegrees, yawDegrees, fieldOfView, fill);
        }

        /// <summary>Distance from the focus centre at which the sphere fills <see cref="Fill"/> of the frame height.</summary>
        public static float ComputeDistance(PortraitFraming framing) {
            float halfAngle = Mathf.Clamp(framing.FieldOfView, 1f, 179f) * 0.5f * Mathf.Deg2Rad;
            float fill = Mathf.Max(framing.Fill, 0.01f);
            return framing.FocusRadius / (fill * Mathf.Tan(halfAngle));
        }

        /// <summary>World camera pose framing a subject posed at <paramref name="subjectPose"/>.</summary>
        public static Pose ComputeCameraPose(PortraitFraming framing, Pose subjectPose) {
            Quaternion rotation = subjectPose.rotation * Quaternion.Euler(framing.PitchDegrees, framing.YawDegrees, 0f);
            Vector3 focus = subjectPose.position + subjectPose.rotation * framing.FocusCentre;
            float distance = ComputeDistance(framing);
            return new Pose(focus - rotation * Vector3.forward * distance, rotation);
        }
    }
}
