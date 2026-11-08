using System;

namespace AlpineLib.Procedural.Landmarks {
    /// <summary>Four spare uniform draws in [0, 1), quantised down to 1e-6, for a consumer's own choices.</summary>
    public readonly struct LandmarkDraws : IEquatable<LandmarkDraws> {
        /// <summary>How many draws there are.</summary>
        public const int Count = 4;

        /// <summary>Stores four draws as given.</summary>
        public LandmarkDraws(double u0, double u1, double u2, double u3) {
            U0 = u0;
            U1 = u1;
            U2 = u2;
            U3 = u3;
        }

        /// <summary>The first draw.</summary>
        public double U0 { get; }

        /// <summary>The second draw.</summary>
        public double U1 { get; }

        /// <summary>The third draw.</summary>
        public double U2 { get; }

        /// <summary>The fourth draw.</summary>
        public double U3 { get; }

        /// <summary>The draw at <paramref name="index"/> in [0, <see cref="Count"/>).</summary>
        public double this[int index] {
            get {
                switch (index) {
                    case 0: return U0;
                    case 1: return U1;
                    case 2: return U2;
                    case 3: return U3;
                    default: throw new ArgumentOutOfRangeException(nameof(index), index, "Must be in [0, 4).");
                }
            }
        }

        /// <inheritdoc />
        public bool Equals(LandmarkDraws other) {
            return U0.Equals(other.U0) && U1.Equals(other.U1) && U2.Equals(other.U2) && U3.Equals(other.U3);
        }

        /// <inheritdoc />
        public override bool Equals(object obj) {
            return obj is LandmarkDraws other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode() {
            unchecked {
                int hash = U0.GetHashCode();
                hash = (hash * 397) ^ U1.GetHashCode();
                hash = (hash * 397) ^ U2.GetHashCode();
                return (hash * 397) ^ U3.GetHashCode();
            }
        }
    }
}
