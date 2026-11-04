using System.Numerics;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>One horizontal cross-section of a <see cref="LoftMesh"/>.</summary>
    /// <remarks>
    /// <see cref="Polygon"/> points are (x, z) pairs around the y axis, ordered by increasing
    /// <c>atan2(z, x)</c> (the order <see cref="LoftMesh.JitterPolygon"/> emits); the opposite order is
    /// detected and handled. Every ring of one loft must have the same point count.
    /// </remarks>
    public readonly struct LoftRing {
        /// <summary>A ring at height <paramref name="y"/>, rotated <paramref name="twist"/> radians about +y.</summary>
        public LoftRing(float y, Vector2[] polygon, float twist = 0f) {
            Y = y;
            Polygon = polygon;
            Twist = twist;
        }

        /// <summary>The ring's height.</summary>
        public float Y { get; }

        /// <summary>The cross-section as (x, z) points. Shared, not copied.</summary>
        public Vector2[] Polygon { get; }

        /// <summary>Rotation of the polygon about +y, in radians (x toward z for positive values).</summary>
        public float Twist { get; }
    }
}
