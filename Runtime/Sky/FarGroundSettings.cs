using System;

namespace AlpineLib.Sky {
    /// <summary>
    /// Tunables of <see cref="FarGround"/>: a world-anchored lattice under the camera, holed where the streamed world
    /// lies and reaching out to the camera's far clip, laid a little below the ground height.
    /// </summary>
    public sealed class FarGroundSettings {
        /// <summary>Vertex spacing (m) of the lattice.</summary>
        public double Spacing { get; set; } = 16.0;

        /// <summary>Step (m) the lattice's anchor snaps to in absolute xz; it is recoloured only when the anchor moves.</summary>
        public double Snap { get; set; } = 32.0;

        /// <summary>Radius about the camera the lattice always leaves to the streamed world.</summary>
        public double InnerRadius { get; set; } = 380.0;

        /// <summary>Radius about the anchor the lattice reaches: past the camera's far clip plus the anchor's snap slack.</summary>
        public double OuterRadius { get; set; } = 2240.0;

        /// <summary>How far below the ground height the lattice lies, so it never shows through streamed ground.</summary>
        public double Drop { get; set; } = 0.3;

        /// <summary>The lattice's hole about its anchor: the inner radius less the farthest the anchor sits from the camera.</summary>
        public double HoleRadius => InnerRadius - Snap * Math.Sqrt(0.5);

        /// <summary>Least distance from the camera the lattice reaches wherever its anchor sits.</summary>
        public double GuaranteedReach => OuterRadius - Snap * Math.Sqrt(0.5);

        public static FarGroundSettings Default => new FarGroundSettings();

        /// <summary>Throws when a value is out of range.</summary>
        public void Validate() {
            RequirePositive(Spacing, nameof(Spacing));
            RequirePositive(Snap, nameof(Snap));
            RequirePositive(InnerRadius, nameof(InnerRadius));
            RequirePositive(HoleRadius, nameof(HoleRadius));
            if (!(OuterRadius > InnerRadius) || double.IsInfinity(OuterRadius)) throw new ArgumentOutOfRangeException(nameof(OuterRadius), OuterRadius, $"Must be finite and above {InnerRadius}.");
            if (!(Drop >= 0.0) || double.IsInfinity(Drop)) throw new ArgumentOutOfRangeException(nameof(Drop), Drop, "Must be finite and not negative.");
        }

        private static void RequirePositive(double value, string name) {
            if (!(value > 0.0) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, value, "Must be positive and finite.");
        }
    }
}
