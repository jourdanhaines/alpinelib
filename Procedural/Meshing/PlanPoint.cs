using System;

namespace AlpineLib.Procedural.Meshing {
    /// <summary>A point on the XZ plan in double precision: one vertex of a <see cref="ConvexPlan"/> polygon.</summary>
    public readonly struct PlanPoint : IEquatable<PlanPoint> {
        /// <summary>Builds a point.</summary>
        public PlanPoint(double x, double z) {
            X = x;
            Z = z;
        }

        /// <summary>World X.</summary>
        public double X { get; }

        /// <summary>World Z.</summary>
        public double Z { get; }

        /// <inheritdoc />
        public bool Equals(PlanPoint other) => X.Equals(other.X) && Z.Equals(other.Z);

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is PlanPoint other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ Z.GetHashCode();

        /// <inheritdoc />
        public override string ToString() => "(" + X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", " + Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")";
    }
}
