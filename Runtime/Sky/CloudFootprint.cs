using UnityEngine;

namespace AlpineLib.Sky {
    /// <summary>One puff of a drawn cloud as its ground shadow sees it: world centre and horizontal radius, metres.</summary>
    public readonly struct CloudFootprint {
        public CloudFootprint(Vector3 centre, float radius) {
            Centre = centre;
            Radius = radius;
        }

        public Vector3 Centre { get; }
        public float Radius { get; }
    }
}
