using UnityEngine;

namespace AlpineLib.Rendering {
    /// <summary>
    /// A ground rectangle seen straight down: <see cref="Centre"/> on the ground, <see cref="Size"/>
    /// metres along world X (east) and Z (north), the camera <see cref="Height"/> above the centre and
    /// clipping at <see cref="NearClip"/>..<see cref="FarClip"/> metres below itself.
    /// </summary>
    public readonly struct TopDownCaptureArea {
        /// <summary>Builds an area.</summary>
        public TopDownCaptureArea(Vector3 centre, Vector2 size, float height, float nearClip, float farClip) {
            Centre = centre;
            Size = size;
            Height = height;
            NearClip = nearClip;
            FarClip = farClip;
        }

        /// <summary>A square area of side <paramref name="side"/> metres.</summary>
        public static TopDownCaptureArea Square(Vector3 centre, float side, float height, float nearClip, float farClip) {
            return new TopDownCaptureArea(centre, new Vector2(side, side), height, nearClip, farClip);
        }

        /// <summary>World-space ground centre.</summary>
        public Vector3 Centre { get; }

        /// <summary>Metres along X (image width) and Z (image height).</summary>
        public Vector2 Size { get; }

        /// <summary>Camera height above <see cref="Centre"/>.</summary>
        public float Height { get; }

        /// <summary>Near clip distance from the camera.</summary>
        public float NearClip { get; }

        /// <summary>Far clip distance from the camera.</summary>
        public float FarClip { get; }
    }
}
