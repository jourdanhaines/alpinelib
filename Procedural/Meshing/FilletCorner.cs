using System;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>How <see cref="PolygonFillet"/> rounds one corner: the arc radius and whether the arc keeps its midpoint.</summary>
    public readonly struct FilletCorner {
        /// <summary>A corner rounded by <paramref name="radius"/>; with <paramref name="hasMidpoint"/> the arc takes three vertices, else two.</summary>
        public FilletCorner(double radius, bool hasMidpoint) {
            if (!(radius >= 0.0)) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be a number at least zero.");

            Radius = radius;
            HasMidpoint = hasMidpoint;
        }

        /// <summary>Arc radius, metres; the tangent length may cap it.</summary>
        public double Radius { get; }

        /// <summary>True for three vertices (both tangent points and the arc's midpoint), false for the two tangent points.</summary>
        public bool HasMidpoint { get; }
    }
}
