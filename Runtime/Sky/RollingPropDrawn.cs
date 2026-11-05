using AlpineLib.Procedural.Sky;
using UnityEngine;

namespace AlpineLib.Sky {
    // A prop a RollingPropField may draw this frame: its entry, its pose and the root of the cell it is in.
    internal readonly struct RollingPropDrawn {
        public RollingPropDrawn(RollingPropEntry entry, RollerPose pose, Transform root) {
            Entry = entry;
            Pose = pose;
            Root = root;
        }

        public RollingPropEntry Entry { get; }
        public RollerPose Pose { get; }
        public Transform Root { get; }
    }
}
