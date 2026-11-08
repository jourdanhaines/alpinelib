using System;

namespace AlpineLib.Netcode.Collision {
    /// <summary>
    /// The plan polygon of a prism <see cref="CollisionShape"/>: up to sixteen (x, z) vertices stored inline as
    /// offsets from the shape's centre, convex and counter-clockwise by sign (shoelace area &gt; 0).
    /// </summary>
    /// <remarks>
    /// Plain fields rather than an array keep the shape blittable and its array one flat block of memory on
    /// both runtimes. Unused slots stay zero. The outward normal of edge (dx, dz) is (dz, −dx).
    /// </remarks>
    public struct PrismPlan {
        /// <summary>Most vertices a plan holds.</summary>
        public const int MaxVertices = 16;

        /// <summary>Fewest vertices a plan holds.</summary>
        public const int MinVertices = 3;

        /// <summary>How many of the vertex slots are used, 3–16 for a valid prism.</summary>
        public byte Count;

        /// <summary>Vertex X offsets from the shape centre, in index order.</summary>
        public float X0, X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, X13, X14, X15;

        /// <summary>Vertex Z offsets from the shape centre, in index order.</summary>
        public float Z0, Z1, Z2, Z3, Z4, Z5, Z6, Z7, Z8, Z9, Z10, Z11, Z12, Z13, Z14, Z15;

        /// <summary>Reads vertex <paramref name="index"/> (0–15).</summary>
        public void GetVertex(int index, out float x, out float z) {
            switch (index) {
                case 0: x = X0; z = Z0; return;
                case 1: x = X1; z = Z1; return;
                case 2: x = X2; z = Z2; return;
                case 3: x = X3; z = Z3; return;
                case 4: x = X4; z = Z4; return;
                case 5: x = X5; z = Z5; return;
                case 6: x = X6; z = Z6; return;
                case 7: x = X7; z = Z7; return;
                case 8: x = X8; z = Z8; return;
                case 9: x = X9; z = Z9; return;
                case 10: x = X10; z = Z10; return;
                case 11: x = X11; z = Z11; return;
                case 12: x = X12; z = Z12; return;
                case 13: x = X13; z = Z13; return;
                case 14: x = X14; z = Z14; return;
                case 15: x = X15; z = Z15; return;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        /// <summary>Writes vertex <paramref name="index"/> (0–15).</summary>
        public void SetVertex(int index, float x, float z) {
            switch (index) {
                case 0: X0 = x; Z0 = z; return;
                case 1: X1 = x; Z1 = z; return;
                case 2: X2 = x; Z2 = z; return;
                case 3: X3 = x; Z3 = z; return;
                case 4: X4 = x; Z4 = z; return;
                case 5: X5 = x; Z5 = z; return;
                case 6: X6 = x; Z6 = z; return;
                case 7: X7 = x; Z7 = z; return;
                case 8: X8 = x; Z8 = z; return;
                case 9: X9 = x; Z9 = z; return;
                case 10: X10 = x; Z10 = z; return;
                case 11: X11 = x; Z11 = z; return;
                case 12: X12 = x; Z12 = z; return;
                case 13: X13 = x; Z13 = z; return;
                case 14: X14 = x; Z14 = z; return;
                case 15: X15 = x; Z15 = z; return;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}
