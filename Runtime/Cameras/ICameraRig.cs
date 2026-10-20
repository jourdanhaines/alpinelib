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
        /// Extra multiplier on queued look input, on top of the rig's sensitivity; 1 by default, never
        /// negative. Lowered while the player steadies the view, e.g. reading a held prop; carried
        /// across rigs on a perspective switch.
        /// </summary>
        float LookGain { get; set; }

        /// <summary>
        /// Eases the pitch by <paramref name="degrees"/> (positive looks down) over <paramref name="seconds"/>
        /// (0 turns at once), on top of the player's own look and within the rig's pitch limits, e.g.
        /// glancing down at a prop raised into the hands. A nudge started while one runs takes over its
        /// remainder. Returns the degrees it will turn from the current pitch once clamped.
        /// </summary>
        float NudgePitch(float degrees, float seconds);

        /// <summary>
        /// Degrees the pitch has actually turned for nudges since the last <see cref="NudgePitch"/>,
        /// after the limits: what a caller undoes to put the view back.
        /// </summary>
        float PitchNudgeApplied { get; }

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
        /// Overwrites the look angles outright, bypassing sensitivity, any queued look input and any running
        /// pitch nudge. Pitch is clamped to the rig's authored range.
        /// </summary>
        void SetLookAngles(float yawDegrees, float pitchDegrees);
    }
}
