using System;

namespace AlpineLib.Procedural.Sky {
    /// <summary>
    /// Tunables of the far ground (<c>AlpineLib.Sky.FarGround</c>): a world-anchored lattice under the camera, holed
    /// where the streamed world lies and reaching past the camera's far clip, laid a little below the ground height;
    /// without a colour source a flat annulus over the same radii about the camera.
    /// </summary>
    public sealed class FarGroundSettings {
        /// <summary>Vertex spacing (m) of the lattice.</summary>
        public double Spacing { get; set; } = 16.0;

        /// <summary>
        /// Step (m) the lattice's anchor snaps to in absolute xz; it is recoloured only when the anchor moves. A whole
        /// multiple of <see cref="Spacing"/>, so a snap shifts known colours across instead of resampling them all.
        /// </summary>
        public double Snap { get; set; } = 32.0;

        /// <summary>Radius about the camera the far ground always leaves to the streamed world.</summary>
        public double InnerRadius { get; set; } = 380.0;

        /// <summary>Radius about the anchor the lattice reaches: past the camera's far clip plus the anchor's snap slack.</summary>
        public double OuterRadius { get; set; } = 2240.0;

        /// <summary>How far below the ground height the far ground lies, so it never shows through streamed ground.</summary>
        public double Drop { get; set; } = 0.3;

        /// <summary>Farthest the snapped anchor sits from the camera: half a snap cell's diagonal.</summary>
        public double SnapSlack => Snap * Math.Sqrt(0.5);

        /// <summary>The lattice's hole about its anchor: the inner radius less the snap slack.</summary>
        public double HoleRadius => InnerRadius - SnapSlack;

        /// <summary>Least distance from the camera the lattice reaches wherever its anchor sits: the outer radius less the snap slack.</summary>
        public double GuaranteedReach => OuterRadius - SnapSlack;

        public static FarGroundSettings Default => new FarGroundSettings();

        /// <summary>A copy, so later edits to this one do not reach a component configured with it.</summary>
        public FarGroundSettings Clone() {
            return new FarGroundSettings {
                Spacing = Spacing,
                Snap = Snap,
                InnerRadius = InnerRadius,
                OuterRadius = OuterRadius,
                Drop = Drop,
            };
        }

        /// <summary>Throws when a value is out of range.</summary>
        public void Validate() {
            RequirePositive(Spacing, nameof(Spacing));
            RequirePositive(Snap, nameof(Snap));
            double steps = Snap / Spacing;
            if (steps != Math.Round(steps)) throw new ArgumentOutOfRangeException(nameof(Snap), Snap, $"Must be a whole multiple of the spacing {Spacing}.");
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
