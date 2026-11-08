using System;
using System.Numerics;
using AlpineLib.Procedural.Meshing;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// The flat far ground drawn when nothing colours it: an annulus from <see cref="FarGroundSettings.InnerRadius"/>
    /// to <see cref="FarGroundSettings.OuterRadius"/> about its centre, facing up, painted one atlas UV.
    /// </summary>
    public static class FarGroundAnnulus {
        /// <summary>Segments round the circle (5° each).</summary>
        public const int Segments = 72;

        // cos of 0°, 5°, … 90°: the quarter circle every segment corner is mirrored from.
        private static readonly double[] QuarterCos = {
            1.000000000000, 0.996194698092, 0.984807753012, 0.965925826289, 0.939692620786, 0.906307787037, 0.866025403784,
            0.819152044289, 0.766044443119, 0.707106781187, 0.642787609687, 0.573576436351, 0.500000000000, 0.422618261741,
            0.342020143326, 0.258819045103, 0.173648177667, 0.087155742748, 0.000000000000,
        };

        private const int QuarterSteps = Segments / 4;

        /// <summary>The annulus of <paramref name="settings"/> in metres about its centre, every face at <paramref name="uv"/>.</summary>
        public static FacetMesh Build(FarGroundSettings settings, Vector2 uv) {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var mesh = new FacetMesh(Segments * 2);
            var inner = (float)settings.InnerRadius;
            var outer = (float)settings.OuterRadius;
            for (int segment = 0; segment < Segments; segment++) {
                Vector3 innerStart = OnCircle(inner, segment);
                Vector3 innerEnd = OnCircle(inner, segment + 1);
                Vector3 outerStart = OnCircle(outer, segment);
                Vector3 outerEnd = OnCircle(outer, segment + 1);
                mesh.AddFace(innerStart, outerEnd, outerStart, uv);
                mesh.AddFace(innerStart, innerEnd, outerEnd, uv);
            }
            return mesh;
        }

        /// <summary>Corner <paramref name="step"/> (of <see cref="Segments"/>, counter-clockwise from +x) on a circle of <paramref name="radius"/>.</summary>
        public static Vector3 OnCircle(float radius, int step) {
            int wrapped = ((step % Segments) + Segments) % Segments;
            int quarter = wrapped / QuarterSteps;
            int within = wrapped % QuarterSteps;
            double cos = QuarterCos[within];
            double sin = QuarterCos[QuarterSteps - within];
            switch (quarter) {
                case 0: return new Vector3(radius * (float)cos, 0f, radius * (float)sin);
                case 1: return new Vector3(-radius * (float)sin, 0f, radius * (float)cos);
                case 2: return new Vector3(-radius * (float)cos, 0f, -radius * (float)sin);
                default: return new Vector3(radius * (float)sin, 0f, -radius * (float)cos);
            }
        }
    }
}
