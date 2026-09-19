using UnityEngine;

namespace AlpineLib.Cameras {
    /// <summary>
    /// What a camera rig needs to know about the thing it is aimed at, beyond its transform.
    /// </summary>
    /// <remarks>
    /// A rig keeps its yaw relative to <see cref="YawFrame"/>, so a target carried round a curve on a
    /// train turns the view with the deck without any per-frame plumbing; a null frame is the world.
    /// <see cref="Height"/> lets an eye ride the top of a capsule that crouches.
    /// </remarks>
    public interface ICameraTarget {
        Transform YawFrame { get; }
        float Height { get; }
    }
}
