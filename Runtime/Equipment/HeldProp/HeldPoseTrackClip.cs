using System;
using UnityEngine;

namespace AlpineLib.Equipment {
    /// <summary>
    /// One baked clip of a held prop's pose in its carrier bone's local space, frame by frame.
    /// </summary>
    /// <remarks>
    /// <c>positions</c> and <c>rotations</c> hold one entry per frame; <c>visible</c> may be empty,
    /// which reads as visible throughout.
    /// </remarks>
    [Serializable]
    public class HeldPoseTrackClip {
        [Tooltip("Name the clip is sampled by, e.g. the animation clip it was baked from.")]
        public string key;

        [Tooltip("Frames per second the clip was baked at.")]
        public float fps = 30f;

        [Tooltip("True for a cycling clip: the last frame blends back into the first.")]
        public bool loop;

        public Vector3[] positions = new Vector3[0];
        public Quaternion[] rotations = new Quaternion[0];
        public bool[] visible = new bool[0];

        /// <summary>Frames usable for sampling: the shorter of the position and rotation tracks.</summary>
        public int FrameCount => positions == null || rotations == null ? 0 : Mathf.Min(positions.Length, rotations.Length);

        /// <summary>Clip length in seconds, 0 when it has no frames or no rate.</summary>
        public float Duration => fps > 0f && FrameCount > 0 ? (loop ? FrameCount : FrameCount - 1) / fps : 0f;
    }
}
