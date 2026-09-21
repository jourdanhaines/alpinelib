using UnityEngine;

namespace AlpineLib.Cameras {
    /// <summary>
    /// A target whose head moves with where it looks, and can say where that puts the eye.
    /// </summary>
    /// <remarks>
    /// Optional, and found the same way as <see cref="ICameraTarget"/>. A first-person rig with no
    /// model pivots in place; one with a model adds the offset to its resting eye, so the view swings
    /// out on the same neck the body bends, rather than the camera reading animated bones and
    /// inheriting every bob of a walk cycle.
    /// </remarks>
    public interface ICameraEyeModel {
        /// <summary>
        /// Displacement of the eye from its resting place, in the target's local space; zero when
        /// looking level.
        /// </summary>
        /// <param name="pitchDegrees">Look pitch, positive down.</param>
        /// <param name="restingEyeLocal">
        /// Where the rig rests the eye when looking level, in the target's local space. A model may
        /// take only what it needs from it — a rig's eye height follows a crouching capsule, which a
        /// model anchored to the skeleton has no use for.
        /// </param>
        Vector3 ResolveEyeOffset(float pitchDegrees, Vector3 restingEyeLocal);
    }
}
