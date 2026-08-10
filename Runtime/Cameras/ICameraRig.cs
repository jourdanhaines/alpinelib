using UnityEngine;

namespace AlpineLib.Cameras {
    /// <summary>
    /// Contract every camera rig honours so games — and
    /// <see cref="CameraPerspectiveController"/> — can steer first- and third-person framing through
    /// one handle. A rig owns its own follow and framing rules; callers only hand it look input, a
    /// target, and occasionally an absolute pair of angles.
    /// </summary>
    /// <remarks>
    /// The angles are readable as well as writable because switching perspective has to carry the
    /// player's aim across rigs: the outgoing rig is asked for its yaw and pitch and the incoming rig is
    /// set to them, so the view continues from where it was instead of snapping back to whatever the
    /// idle rig was left holding.
    ///
    /// Rigs are expected to publish their camera pose through <see cref="CameraAnchor"/> rather than
    /// moving a camera themselves. That keeps a single camera shared between rigs — the one thing that
    /// makes a blend between two perspectives possible at all — and leaves rigs free to run every frame
    /// whether or not they are the one being looked through.
    /// </remarks>
    public interface ICameraRig {
        /// <summary>
        /// Current yaw in degrees.
        /// </summary>
        float Yaw { get; }

        /// <summary>
        /// Current pitch in degrees, already clamped to whatever range the rig authorises.
        /// </summary>
        float Pitch { get; }

        /// <summary>
        /// Where the rig is looking, flattened onto the ground plane and normalised. Use this to make
        /// movement input camera-relative.
        /// </summary>
        Vector3 PlanarForward { get; }

        /// <summary>
        /// Right of <see cref="PlanarForward"/>, flattened onto the ground plane and normalised.
        /// </summary>
        Vector3 PlanarRight { get; }

        /// <summary>
        /// Transform carrying the pose a camera should adopt to look through this rig. Rewritten every
        /// <c>LateUpdate</c>; never parent anything to it that should stay put.
        /// </summary>
        Transform CameraAnchor { get; }

        /// <summary>
        /// Queues a look delta in degrees — X yaws, positive Y looks up. Deltas accumulate across the
        /// frame and are consumed once in <c>LateUpdate</c>.
        /// </summary>
        void AddLookInput(Vector2 degreesDelta);

        /// <summary>
        /// Points the rig at a new target, or at nothing when passed null.
        /// </summary>
        void SetTarget(Transform target);

        /// <summary>
        /// Overwrites the look angles outright, bypassing sensitivity and any queued look input. Pitch is
        /// clamped to the rig's authored range.
        /// </summary>
        void SetLookAngles(float yawDegrees, float pitchDegrees);
    }
}
