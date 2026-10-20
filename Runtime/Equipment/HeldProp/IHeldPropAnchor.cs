using UnityEngine;

namespace AlpineLib.Equipment {
    /// <summary>
    /// Says where a held prop's root belongs in the world, for <see cref="HeldPropPresenter"/>.
    /// </summary>
    /// <remarks>
    /// Prop frame convention shared by every anchor: local +X runs from the holder's left grip to the
    /// right grip, +Y is up on the readable face, and the readable face looks down local −Z toward the
    /// holder's eye (the same convention as <see cref="AlpineLib.Cameras.CameraFacingPose"/>).
    /// </remarks>
    public interface IHeldPropAnchor {
        /// <summary>True when the pose follows something moving and must be re-resolved every frame.</summary>
        bool IsContinuous { get; }

        /// <summary>World pose for the prop's root; false when the anchor cannot resolve one this frame.</summary>
        bool TryResolvePose(out Pose pose);
    }
}
