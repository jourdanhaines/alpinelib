using System.Collections.Generic;
using UnityEngine;

namespace AlpineLib.Equipment {
    /// <summary>
    /// A held prop's pose baked from authored clips, relative to the bone that carries it, for
    /// <see cref="BonePoseAnchor"/> to place the prop with.
    /// </summary>
    /// <remarks>
    /// Sampling by normalised time: a one-shot clip maps 0..1 onto its first to last frame, a looping
    /// clip onto a full cycle that wraps from the last frame back to the first. Positions are lerped,
    /// rotations slerped, visibility taken from the nearest frame.
    /// </remarks>
    [CreateAssetMenu(fileName = "HeldPoseTrack", menuName = "AlpineLib/Equipment/Held Pose Track")]
    public class HeldPoseTrack : ScriptableObject {
        [Tooltip("Uniform scale the prop is held at.")]
        [SerializeField] private float heldScale = 1f;

        [Tooltip("View pitch, in degrees, the clips were authored for.")]
        [SerializeField] private float viewPitchDegrees;

        [Tooltip("Bone the poses are relative to.")]
        [SerializeField] private HumanBodyBones carrierBone = HumanBodyBones.Spine;

        [SerializeField] private HeldPoseTrackClip[] clips = new HeldPoseTrackClip[0];

        public float HeldScale => heldScale;
        public float ViewPitchDegrees => viewPitchDegrees;
        public HumanBodyBones CarrierBone => carrierBone;
        public IReadOnlyList<HeldPoseTrackClip> Clips => clips ?? new HeldPoseTrackClip[0];

        /// <summary>Sets the track-wide values, e.g. from a baker.</summary>
        public void Configure(float scale, float pitchDegrees, HumanBodyBones bone) {
            heldScale = scale;
            viewPitchDegrees = pitchDegrees;
            carrierBone = bone;
        }

        /// <summary>Replaces every clip, e.g. from a baker.</summary>
        public void SetClips(HeldPoseTrackClip[] bakedClips) {
            clips = bakedClips ?? new HeldPoseTrackClip[0];
        }

        /// <summary>The clip named <paramref name="key"/>; false when there is none.</summary>
        public bool TryGetClip(string key, out HeldPoseTrackClip clip) {
            clip = null;
            if (clips == null || string.IsNullOrEmpty(key)) return false;

            foreach (HeldPoseTrackClip candidate in clips) {
                if (candidate == null || candidate.key != key) continue;

                clip = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Carrier-local pose and visibility of clip <paramref name="key"/> at
        /// <paramref name="normalizedTime"/>; false when the clip is missing or has no frames.
        /// </summary>
        public bool TrySample(string key, float normalizedTime, out Pose local, out bool visible) {
            local = Pose.identity;
            visible = false;
            if (!TryGetClip(key, out HeldPoseTrackClip clip)) return false;

            return TrySample(clip, normalizedTime, out local, out visible);
        }

        /// <summary>As <see cref="TrySample(string, float, out Pose, out bool)"/>, for a clip already found.</summary>
        public static bool TrySample(HeldPoseTrackClip clip, float normalizedTime, out Pose local, out bool visible) {
            local = Pose.identity;
            visible = false;
            int frameCount = clip != null ? clip.FrameCount : 0;
            if (frameCount == 0 || !float.IsFinite(normalizedTime)) return false;

            float frame = FramePosition(clip.loop, frameCount, normalizedTime);
            int from = Mathf.FloorToInt(frame);
            float blend = frame - from;
            from = WrapFrame(clip.loop, frameCount, from);
            int to = WrapFrame(clip.loop, frameCount, from + 1);

            local = new Pose(
                Vector3.Lerp(clip.positions[from], clip.positions[to], blend),
                Quaternion.Slerp(clip.rotations[from], clip.rotations[to], blend));
            visible = SampleVisible(clip, WrapFrame(clip.loop, frameCount, Mathf.RoundToInt(frame)));
            return true;
        }

        // One-shot: 0..1 spans first to last frame. Loop: 0..1 spans a full cycle of every frame.
        private static float FramePosition(bool loop, int frameCount, float normalizedTime) {
            if (loop) return Mathf.Repeat(normalizedTime, 1f) * frameCount;

            return Mathf.Clamp01(normalizedTime) * (frameCount - 1);
        }

        private static int WrapFrame(bool loop, int frameCount, int frame) {
            if (loop) return ((frame % frameCount) + frameCount) % frameCount;

            return Mathf.Clamp(frame, 0, frameCount - 1);
        }

        private static bool SampleVisible(HeldPoseTrackClip clip, int frame) {
            if (clip.visible == null || clip.visible.Length == 0) return true;

            return clip.visible[Mathf.Min(frame, clip.visible.Length - 1)];
        }
    }
}
